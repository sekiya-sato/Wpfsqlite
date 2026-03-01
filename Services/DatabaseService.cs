using Microsoft.Data.Sqlite;
using System.Data;
namespace GikoSqlite.Services;


public class DatabaseService {
	public List<string> GetTableNames(string path) {
		var tables = new List<string>();
		using var conn = new SqliteConnection($"Data Source={path}");
		conn.Open();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
		using var rdr = cmd.ExecuteReader();
		while (rdr.Read()) {
			tables.Add(rdr.GetString(0));
		}
		return tables;
	}

	public DataTable GetTableData(string path, string tableName) {
		var dt = new DataTable();
		using var conn = new SqliteConnection($"Data Source={path}");
		conn.Open();
		using var cmd = conn.CreateCommand();
		// include rowid as __rowid to be able to identify rows
		cmd.CommandText = $"SELECT rowid AS __rowid, * FROM \"{tableName}\" LIMIT 1000"; // limit for responsiveness
		using var rdr = cmd.ExecuteReader();
		dt.Load(rdr);
		return dt;
	}

	public DataTable ExecuteQuery(string path, string sql) {
		var dt = new DataTable();
		using var conn = new SqliteConnection($"Data Source={path}");
		conn.Open();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = sql;
		using var rdr = cmd.ExecuteReader();
		dt.Load(rdr);
		return dt;
	}

	public DataTable GetTableColumns(string path, string tableName) {
		var dt = new DataTable();
		using var conn = new SqliteConnection($"Data Source={path}");
		conn.Open();
		using var cmd = conn.CreateCommand();
		// PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
		cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
		using var rdr = cmd.ExecuteReader();
		dt.Load(rdr);
		return dt;
	}

	public class KeyInfo {
		public string Name { get; set; } = string.Empty;
		public string Columns { get; set; } = string.Empty;
		public bool Unique { get; set; }
		public string SecondLine => $"  {Columns} ({(Unique ? "Unique" : "NonUnique")})";
	}

	public List<KeyInfo> GetAllKeys(string path) {
		var result = new List<KeyInfo>();
		using var conn = new SqliteConnection($"Data Source={path}");
		conn.Open();

		// get tables
		using var cmdTables = conn.CreateCommand();
		cmdTables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
		using var rdrTables = cmdTables.ExecuteReader();
		var tables = new List<string>();
		while (rdrTables.Read()) tables.Add(rdrTables.GetString(0));

		foreach (var table in tables) {
			using var cmd = conn.CreateCommand();
			cmd.CommandText = $"PRAGMA index_list(\"{table}\")";
			using var rdr = cmd.ExecuteReader();
			var indexes = new List<(string name, bool unique)>();
			while (rdr.Read()) {
				// columns: seq, name, unique
				var name = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1);
				var unique = false;
				try { unique = rdr.GetInt32(2) == 1; } catch { }
				if (!string.IsNullOrEmpty(name)) indexes.Add((name, unique));
			}

			foreach (var idx in indexes) {
				using var cmdIdx = conn.CreateCommand();
				cmdIdx.CommandText = $"PRAGMA index_info(\"{idx.name}\")";
				using var rdrIdx = cmdIdx.ExecuteReader();
				var cols = new List<string>();
				while (rdrIdx.Read()) {
					// seqno, cid, name
					if (!rdrIdx.IsDBNull(2)) cols.Add(rdrIdx.GetString(2));
				}
				var ki = new KeyInfo {
					Name = $"{table}.{idx.name}",
					Columns = string.Join(",", cols),
					Unique = idx.unique
				};
				result.Add(ki);
			}
		}

		return result;
	}

	public void UpdateRow(string path, string tableName, long rowid, Dictionary<string, object?> values) {
		using var conn = new SqliteConnection($"Data Source={path}");
		conn.Open();
		using var cmd = conn.CreateCommand();

		var sets = new List<string>();
		int i = 0;
		foreach (var kv in values) {
			sets.Add($"\"{kv.Key}\" = @p{i}");
			cmd.Parameters.AddWithValue($"@p{i}", kv.Value ?? DBNull.Value);
			i++;
		}

		if (rowid > 0) {
			cmd.CommandText = $"UPDATE \"{tableName}\" SET {string.Join(",", sets)} WHERE rowid = @id";
			cmd.Parameters.AddWithValue("@id", rowid);
			cmd.ExecuteNonQuery();
		}
		else {
			// No rowid provided: try best-effort update (not implemented)
		}
	}

	public void DeleteRow(string path, string tableName, long rowid) {
		using var conn = new SqliteConnection($"Data Source={path}");
		conn.Open();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = $"DELETE FROM \"{tableName}\" WHERE rowid = @id";
		cmd.Parameters.AddWithValue("@id", rowid);
		cmd.ExecuteNonQuery();
	}

	public long InsertRow(string path, string tableName, Dictionary<string, object?> values) {
		using var conn = new SqliteConnection($"Data Source={path}");
		conn.Open();
		using var cmd = conn.CreateCommand();
		var cols = new List<string>();
		var ps = new List<string>();
		int i = 0;
		foreach (var kv in values) {
			cols.Add($"\"{kv.Key}\"");
			ps.Add($"@p{i}");
			cmd.Parameters.AddWithValue($"@p{i}", kv.Value ?? DBNull.Value);
			i++;
		}
		cmd.CommandText = $"INSERT INTO \"{tableName}\" ({string.Join(",", cols)}) VALUES ({string.Join(",", ps)})";
		cmd.ExecuteNonQuery();
		using var cmd2 = conn.CreateCommand();
		cmd2.CommandText = "SELECT last_insert_rowid()";
		var obj = cmd2.ExecuteScalar();
		return obj == null ? 0 : Convert.ToInt64(obj);
	}
}
