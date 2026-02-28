using Microsoft.Win32;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wpfsqlite.ViewModels;

namespace Wpfsqlite {
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window {
		private readonly MainViewModel _vm;

		public MainWindow() {
			InitializeComponent();
			_vm = new MainViewModel();
			DataContext = _vm;
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

		private async void ExecuteSql_Click(object sender, RoutedEventArgs e) {
			var sql = QueryTextBox?.Text;
			if (string.IsNullOrWhiteSpace(sql)) return;
			_vm.AddSqlHistory(sql);
			await _vm.ExecuteQueryAsync(sql);
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

		private void ListArea_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if (ListArea.SelectedItem is System.Data.DataRowView drv) {
				_vm.SetCurrentFromDataRow(drv);
				// switch to Edit tab when a row is selected
				try {
					if (TableListArea != null && EditTab != null) {
						TableListArea.SelectedItem = EditTab;
					}
				}
				catch {
					// ignore if UI elements not available
				}
			}
			else {
				_vm.SetCurrentFromDataRow(null);
			}
		}

		private async void ModifyButton_Click(object sender, RoutedEventArgs e) {
			await _vm.SaveCurrentEditAsync();
		}

		private async void AddButton_Click(object sender, RoutedEventArgs e) {
			await _vm.AddNewAsync();
		}

		private async void DeleteButton_Click(object sender, RoutedEventArgs e) {
			await _vm.DeleteCurrentAsync();
		}
	}
}
