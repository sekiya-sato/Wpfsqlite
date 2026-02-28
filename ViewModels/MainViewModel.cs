using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;

namespace Wpfsqlite.ViewModels;

public class ColumnInfo : ObservableObject {
	public string Name { get; set; } = string.Empty;
	public string Type { get; set; } = string.Empty;
	public bool NotNull { get; set; }
	public bool PrimaryKey { get; set; }

	private string? _editValue;
	public string? EditValue {
		get => _editValue;
		set => SetProperty(ref _editValue, value);
	}

	// store original value for revert / dirty-check
	public string? OriginalValue { get; set; }
}


public partial class MainViewModel : ObservableObject {
	private readonly Services.DatabaseService _dbService = new Services.DatabaseService();

	public ObservableCollection<string> Tables { get; } = new ObservableCollection<string>();

	private DataView? _selectedTableView;
	public DataView? SelectedTableView {
		get => _selectedTableView;
		private set => SetProperty(ref _selectedTableView, value);
	}

	[CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
	private int _selectedTabIndex;

	[CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
	private System.Data.DataRowView? _selectedRow;

	[CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
	private ColumnInfo? _selectedColumn;

	partial void OnSelectedRowChanged(System.Data.DataRowView? value) {
		// update current row in viewmodel and switch to Edit tab when a row is selected
		SetCurrentFromDataRow(value);
		if (value != null) SelectedTabIndex = 2; // Edit tab index (Tables=0, Keys=1, Edit=2)
	}

	/// <summary>
	/// Close the current database and clear UI state.
	/// </summary>
	public void CloseDatabase() {
		_currentDatabasePath = null;
		Tables.Clear();
		Columns.Clear();
		Keys.Clear();
		SelectedTable = null;
		SelectedTableView = null;
	}

	private string? _selectedTable;
	public string? SelectedTable {
		get => _selectedTable;
		set {
			if (SetProperty(ref _selectedTable, value)) {
				_ = LoadSelectedTableAsync();
			}
		}
	}

	public ObservableCollection<string> History { get; } = new ObservableCollection<string>();
	public ObservableCollection<ColumnInfo> Columns { get; } = new ObservableCollection<ColumnInfo>();
	public ObservableCollection<Services.DatabaseService.KeyInfo> Keys { get; } = new ObservableCollection<Services.DatabaseService.KeyInfo>();
	public ObservableCollection<string> SqlHistory { get; } = new ObservableCollection<string>();

	private readonly List<ColumnInfo> _subscribedColumns = new List<ColumnInfo>();

	private bool _hasPendingEdits;
	public bool HasPendingEdits {
		get => _hasPendingEdits;
		private set => SetProperty(ref _hasPendingEdits, value);
	}

	private string? _currentDatabasePath;
	private const string HistoryFileName = "history.json";
	private const string SqlHistoryFileName = "sqlhistory.json";

	public MainViewModel() {
		LoadHistory();
		LoadSqlHistory();
	}

	private void LoadSqlHistory() {
		try {
			if (File.Exists(SqlHistoryFileName)) {
				var json = File.ReadAllText(SqlHistoryFileName);
				var list = System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
				SqlHistory.Clear();
				foreach (var s in list) SqlHistory.Add(s);
			}
		}
		catch { }
	}

	private void SaveSqlHistory() {
		try {
			var arr = new string[SqlHistory.Count];
			SqlHistory.CopyTo(arr, 0);
			var json = System.Text.Json.JsonSerializer.Serialize(arr, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(SqlHistoryFileName, json);
		}
		catch { }
	}

	public void AddSqlHistory(string sql) {
		if (string.IsNullOrWhiteSpace(sql)) return;
		// normalize SQL for duplicate detection
		var norm = NormalizeSql(sql);
		for (int i = 0; i < SqlHistory.Count; i++) {
			var existing = SqlHistory[i];
			if (string.Equals(norm, NormalizeSql(existing), StringComparison.Ordinal)) {
				// move existing to top
				SqlHistory.RemoveAt(i);
				SqlHistory.Insert(0, existing);
				SaveSqlHistory();
				return;
			}
		}

		// insert new SQL (keep original formatting)
		SqlHistory.Insert(0, sql);
		SaveSqlHistory();
	}

	private static string NormalizeSql(string sql) {
		if (string.IsNullOrWhiteSpace(sql)) return string.Empty;
		// trim
		var s = sql.Trim();
		// remove trailing semicolons
		s = s.TrimEnd(';').Trim();
		// collapse whitespace (spaces, newlines, tabs) to single space
		s = Regex.Replace(s, "\\s+", " ");
		// lowercase for case-insensitive comparison
		s = s.ToLowerInvariant();
		return s;
	}


	private void LoadHistory() {
		try {
			if (File.Exists(HistoryFileName)) {
				var json = File.ReadAllText(HistoryFileName);
				var list = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
				History.Clear();
				foreach (var p in list) History.Add(p);
			}
		}
		catch { }
	}

	private void SaveHistory() {
		try {
			var arr = new string[History.Count];
			History.CopyTo(arr, 0);
			var json = JsonSerializer.Serialize(arr, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(HistoryFileName, json);
		}
		catch { }
	}

	public async Task OpenDatabaseAsync(string path) {
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
		_currentDatabasePath = path;
		var tables = await Task.Run(() => _dbService.GetTableNames(path));
		Tables.Clear();
		foreach (var t in tables) Tables.Add(t);

		// load keys/index info for display
		try {
			var keys = await Task.Run(() => _dbService.GetAllKeys(path));
			Keys.Clear();
			foreach (var k in keys) Keys.Add(k);
		}
		catch { }

		if (!History.Contains(path)) History.Insert(0, path);
		else {
			History.Remove(path);
			History.Insert(0, path);
		}
		SaveHistory();
	}

	private async Task LoadSelectedTableAsync() {
		if (string.IsNullOrWhiteSpace(_currentDatabasePath) || string.IsNullOrWhiteSpace(SelectedTable)) return;
		try {
			var dt = await Task.Run(() => _dbService.GetTableData(_currentDatabasePath!, SelectedTable!));
			SelectedTableView = dt.DefaultView;
		}
		catch { }
		// load column details
		try {
			var cols = await Task.Run(() => _dbService.GetTableColumns(_currentDatabasePath!, SelectedTable!));
			Columns.Clear();
			foreach (DataRow r in cols.Rows) {
				var ci = new ColumnInfo();
				ci.Name = r.Table.Columns.Contains("name") && !Convert.IsDBNull(r["name"]) ? r["name"].ToString() ?? string.Empty : string.Empty;
				ci.Type = r.Table.Columns.Contains("type") && !Convert.IsDBNull(r["type"]) ? r["type"].ToString() ?? string.Empty : string.Empty;
				ci.NotNull = r.Table.Columns.Contains("notnull") && !Convert.IsDBNull(r["notnull"]) && Convert.ToInt32(r["notnull"]) == 1;
				ci.PrimaryKey = r.Table.Columns.Contains("pk") && !Convert.IsDBNull(r["pk"]) && Convert.ToInt32(r["pk"]) == 1;
				ci.EditValue = string.Empty;
				Columns.Add(ci);
			}
		}
		catch { }
	}

	private DataRowView? _currentRow;

	public void SetCurrentFromDataRow(DataRowView? drv) {
		_currentRow = drv;
		// populate Columns.EditValue
		if (drv == null) {
			foreach (var c in Columns) {
				c.EditValue = string.Empty;
				c.OriginalValue = string.Empty;
			}
			UnsubscribeColumnChange();
			return;
		}

		UnsubscribeColumnChange();
			foreach (var c in Columns) {
				try {
					if (drv.Row.Table.Columns.Contains(c.Name) && drv.Row[c.Name] != null && drv.Row[c.Name] != DBNull.Value) {
						var raw = drv.Row[c.Name].ToString() ?? string.Empty;
						c.EditValue = DecodeEscapedUnicode(raw);
					}
					else {
						c.EditValue = string.Empty;
					}

					c.OriginalValue = c.EditValue;
				}
				catch {
					c.EditValue = string.Empty;
					c.OriginalValue = string.Empty;
				}
			}

		SubscribeColumnChange();
		HasPendingEdits = false;
	}

	private void SubscribeColumnChange() {
		_subscribedColumns.Clear();
		foreach (var c in Columns) {
			c.PropertyChanged += Column_PropertyChanged;
			_subscribedColumns.Add(c);
		}
	}

	private void UnsubscribeColumnChange() {
		foreach (var c in _subscribedColumns) {
			c.PropertyChanged -= Column_PropertyChanged;
		}
		_subscribedColumns.Clear();
	}

	private void Column_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(ColumnInfo.EditValue)) {
			// if any column differs from its original, mark dirty
			foreach (var c in Columns) {
				var orig = c.OriginalValue ?? string.Empty;
				var cur = c.EditValue ?? string.Empty;
				if (!string.Equals(orig, cur, StringComparison.Ordinal)) {
					HasPendingEdits = true;
					return;
				}
			}
			HasPendingEdits = false;
		}
	}

	[RelayCommand]
	public async Task SaveCurrentEditAsync() {
		if (_currentRow == null || string.IsNullOrWhiteSpace(_currentDatabasePath) || string.IsNullOrWhiteSpace(SelectedTable)) return;
		try {
			// get rowid if present
			object? rowidObj = null;
			if (_currentRow.Row.Table.Columns.Contains("__rowid")) rowidObj = _currentRow.Row["__rowid"];

			long? rowid = null;
			if (rowidObj != null && rowidObj != DBNull.Value) rowid = Convert.ToInt64(rowidObj);

			var dict = new Dictionary<string, object?>();
			foreach (var c in Columns) dict[c.Name] = c.EditValue;

			if (rowid.HasValue) {
				await Task.Run(() => _dbService.UpdateRow(_currentDatabasePath!, SelectedTable!, rowid.Value, dict));
			}
			else {
				// fallback: try to find PK column
				await Task.Run(() => _dbService.UpdateRow(_currentDatabasePath!, SelectedTable!, 0, dict));
			}

			await LoadSelectedTableAsync();
			HasPendingEdits = false;
		}
		catch { }
	}

	[RelayCommand]
	public async Task DeleteCurrentAsync() {
		if (_currentRow == null || string.IsNullOrWhiteSpace(_currentDatabasePath) || string.IsNullOrWhiteSpace(SelectedTable)) return;
		try {
			object? rowidObj = null;
			if (_currentRow.Row.Table.Columns.Contains("__rowid")) rowidObj = _currentRow.Row["__rowid"];
			if (rowidObj != null && rowidObj != DBNull.Value) {
				var rowid = Convert.ToInt64(rowidObj);
				await Task.Run(() => _dbService.DeleteRow(_currentDatabasePath!, SelectedTable!, rowid));
			}
			else {
				// fallback: not implemented
			}

			await LoadSelectedTableAsync();
			HasPendingEdits = false;
		}
		catch { }
	}

	[RelayCommand]
	public async Task AddNewAsync() {
		if (string.IsNullOrWhiteSpace(_currentDatabasePath) || string.IsNullOrWhiteSpace(SelectedTable)) return;
		try {
			var dict = new Dictionary<string, object?>();
			foreach (var c in Columns) dict[c.Name] = c.EditValue;
			var newId = await Task.Run(() => _dbService.InsertRow(_currentDatabasePath!, SelectedTable!, dict));
			await LoadSelectedTableAsync();
			HasPendingEdits = false;
		}
		catch { }
	}

	/// <summary>
	/// Discard pending edits and restore EditValue from OriginalValue.
	/// </summary>
	public void DiscardPendingEdits() {
		foreach (var c in Columns) {
			c.EditValue = c.OriginalValue;
		}
		HasPendingEdits = false;
	}

	[RelayCommand]
	public async Task ExecuteQueryAsync(string sql) {
		if (string.IsNullOrWhiteSpace(sql) || string.IsNullOrWhiteSpace(_currentDatabasePath)) return;
		try {
			AddSqlHistory(sql);
			var dt = await Task.Run(() => _dbService.ExecuteQuery(_currentDatabasePath!, sql));
			SelectedTableView = dt.DefaultView;
		}
		catch { }
	}

    /// <summary>
    /// Convert escaped unicode sequences (e.g. "\\u6CC9\\u3000\\u3042\\u3086") into actual characters.
    /// Uses JSON string unescape as primary method, falls back to Regex.Unescape.
    /// </summary>
    private static string DecodeEscapedUnicode(string? input) {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
        // quick check for common escape markers
        if (!input.Contains("\\u") && !input.Contains("\\U") && !input.Contains("\\x")) return input;

        // If the input looks like JSON (object/array), parse and re-serialize using a relaxed encoder
        // so that \u escapes are converted to actual characters.
        var trimmed = input.TrimStart();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("[")) {
            try {
                var obj = JsonSerializer.Deserialize<object>(input);
                if (obj != null) {
                    var options = new JsonSerializerOptions {
                        WriteIndented = false,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    var serialized = JsonSerializer.Serialize(obj, options);
                    return serialized;
                }
            }
            catch { /* fallthrough to other strategies */ }
        }

        try {
            // prepare a JSON string literal: escape backslashes and quotes
            var jsonLiteral = '"' + input.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';
            var decoded = JsonSerializer.Deserialize<string>(jsonLiteral);
            if (!string.IsNullOrEmpty(decoded)) return decoded;
        }
        catch { }

        try {
            return Regex.Unescape(input);
        }
        catch {
            return input;
        }
    }
}
