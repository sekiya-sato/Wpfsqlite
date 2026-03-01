using Microsoft.Win32;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using GikoSqlite.ViewModels;

namespace GikoSqlite;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window {
	private readonly MainViewModel _vm;

	public MainWindow() {
		InitializeComponent();
		_vm = new MainViewModel();
		DataContext = _vm;

		if (DesignerProperties.GetIsInDesignMode(this)) {
			_vm.Columns.Clear();
			for (var i = 1; i <= 10; i++) {
				_vm.Columns.Add(new ColumnInfo {
					Name = $"DummyColumn{i}",
					Type = "TEXT",
					EditValue = $"Sample {i}",
					NotNull = false,
					PrimaryKey = i == 1
				});
			}
		}
		// populate recent menu
		_vm.History.CollectionChanged += History_CollectionChanged;
		PopulateRecentMenu();
		_vm.SqlHistory.CollectionChanged += SqlHistory_CollectionChanged;
		PopulateSqlHistoryMenu();
	}

	private async void OpenMenu_Click(object sender, RoutedEventArgs e) {
		var dlg = new OpenFileDialog { Filter = "SQLite files (*.db;*.sqlite)|*.db;*.sqlite|All files (*.*)|*.*" };
		if (dlg.ShowDialog(this) == true) {
			await _vm.OpenDatabaseAsync(dlg.FileName);
		}

	}

	private void SqlHistory_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
		Dispatcher.Invoke(PopulateSqlHistoryMenu);
	}

	private void PopulateSqlHistoryMenu() {
		if (SqlHistoryMenu == null) return;
		SqlHistoryMenu.Items.Clear();
		foreach (var sql in _vm.SqlHistory) {
			var display = sql.Length > 80 ? sql.Substring(0, 80) + "..." : sql;
			var mi = new MenuItem { Header = display, Tag = sql };
			mi.Click += SqlHistoryMenuItem_Click;
			SqlHistoryMenu.Items.Add(mi);
		}
		if (SqlHistoryMenu.Items.Count == 0) {
			var mi = new MenuItem { Header = "(empty)", IsEnabled = false };
			SqlHistoryMenu.Items.Add(mi);
		}
	}

	private async void SqlHistoryMenuItem_Click(object? sender, RoutedEventArgs e) {
		if (sender is MenuItem mi && mi.Tag is string sql) {
			QueryTextBox.Text = sql;
			// execute selected history SQL
			await _vm.ExecuteQueryAsync(sql);
		}
	}

	private void ExitMenu_Click(object sender, RoutedEventArgs e) {
		Close();
	}


	private async void CloseMenu_Click(object sender, RoutedEventArgs e) {
		if (_vm.HasPendingEdits) {
			var result = MessageBox.Show(this, "保存されていない変更があります。保存しますか?", "確認", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
			if (result == MessageBoxResult.Cancel) {
				return;
			}
			if (result == MessageBoxResult.Yes) {
				await _vm.SaveCurrentEditAsync();
			}
			else if (result == MessageBoxResult.No) {
				_vm.DiscardPendingEdits();
			}
		}

		_vm.CloseDatabase();
		// clear UI elements
		if (QueryTextBox != null) QueryTextBox.Text = string.Empty;
		ListArea.SelectedItem = null;
	}

	private void History_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
		Dispatcher.Invoke(PopulateRecentMenu);
	}

	private void PopulateRecentMenu() {
		if (OpenRecentMenu == null) return;
		OpenRecentMenu.Items.Clear();
		foreach (var path in _vm.History) {
			var fileName = Path.GetFileName(path);
			if (File.Exists(path)) {
				var mi = new MenuItem { Header = fileName, Tag = path };
				mi.Click += RecentMenuItem_Click;
				mi.Icon = new System.Windows.Controls.TextBlock { Text = "📁", Margin = new System.Windows.Thickness(0, 0, 6, 0) };
				OpenRecentMenu.Items.Add(mi);
			}
			else {
				var mi = new MenuItem { Header = fileName + " (missing)", IsEnabled = false };
				mi.Icon = new System.Windows.Controls.TextBlock { Text = "📁", Margin = new System.Windows.Thickness(0, 0, 6, 0) };
				OpenRecentMenu.Items.Add(mi);
			}
		}
		if (OpenRecentMenu.Items.Count == 0) {
			var mi = new MenuItem { Header = "(empty)", IsEnabled = false };
			OpenRecentMenu.Items.Add(mi);
		}
	}

	private async void RecentMenuItem_Click(object? sender, RoutedEventArgs e) {
		if (sender is MenuItem mi && mi.Tag is string path) {
			if (File.Exists(path)) {
				await _vm.OpenDatabaseAsync(path);
			}
		}
	}

	// Selection and button actions are handled by ViewModel via bindings

	private async void Window_Loaded(object sender, RoutedEventArgs e) {
		try {
			var err = await _vm.TryOpenMostRecentAsync();
			if (!string.IsNullOrEmpty(err)) {
				MessageBox.Show(this, $"最近のファイルを開けませんでした: {err}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		catch (System.Exception ex) {
			MessageBox.Show(this, $"起動時にエラーが発生しました: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}
}
