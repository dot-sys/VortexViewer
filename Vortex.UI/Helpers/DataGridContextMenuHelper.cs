using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

// Helper utilities for UI operations
namespace Vortex.UI.Helpers
{
    // Helper for datagrid context menu operations
    public static class DataGridContextMenuHelper
    {
        // Copies current cell value to clipboard
        public static void CopyValue(DataGrid dataGrid)
        {
            if (dataGrid?.CurrentCell == null)
                return;

            try
            {
                var cellContent = dataGrid.CurrentCell.Column?.GetCellContent(dataGrid.CurrentCell.Item);
                if (cellContent is TextBlock textBlock)
                {
                    // Get the full value from the data item instead of truncated display text
                    var item = dataGrid.CurrentCell.Item;
                    var columnIndex = dataGrid.CurrentCell.Column.DisplayIndex;
                    
                    string textToCopy = null;
                    
                    // Check if the item has a FullValue property (for dashboard items)
                    var fullValueProperty = item?.GetType().GetProperty("FullValue");
                    if (fullValueProperty != null && columnIndex == 1) // Value column is usually index 1
                    {
                        textToCopy = fullValueProperty.GetValue(item)?.ToString();
                    }
                    
                    // Fallback to the displayed text if FullValue is not available
                    if (string.IsNullOrEmpty(textToCopy))
                    {
                        textToCopy = textBlock.Text;
                    }
                    
                    if (!string.IsNullOrEmpty(textToCopy))
                    {
                        Clipboard.SetText(textToCopy);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy value: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Copies entire row data to clipboard
        public static void CopyRow(DataGrid dataGrid)
        {
            if (dataGrid?.SelectedItem == null)
                return;

            try
            {
                var rowData = new System.Text.StringBuilder();
                var item = dataGrid.SelectedItem;

                foreach (var column in dataGrid.Columns)
                {
                    var cellContent = column.GetCellContent(item);
                    if (cellContent is TextBlock textBlock)
                    {
                        if (rowData.Length > 0)
                            rowData.Append("\t");
                        
                        // Use FullValue for value column if available
                        var columnIndex = column.DisplayIndex;
                        var fullValueProperty = item?.GetType().GetProperty("FullValue");
                        
                        if (fullValueProperty != null && columnIndex == 1) // Value column
                        {
                            var fullValue = fullValueProperty.GetValue(item)?.ToString();
                            rowData.Append(fullValue ?? textBlock.Text);
                        }
                        else
                        {
                            rowData.Append(textBlock.Text);
                        }
                    }
                }

                if (rowData.Length > 0)
                {
                    Clipboard.SetText(rowData.ToString());
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy row: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Opens file path in explorer
        public static void GoToPath(DataGrid dataGrid)
        {
            if (dataGrid?.CurrentCell == null)
                return;

            try
            {
                var cellContent = dataGrid.CurrentCell.Column?.GetCellContent(dataGrid.CurrentCell.Item);
                if (cellContent is TextBlock textBlock)
                {
                    string path = textBlock.Text?.Trim();
                    if (string.IsNullOrEmpty(path))
                        return;

                    string directoryPath = GetDirectoryPath(path);

                    if (!string.IsNullOrEmpty(directoryPath) && Directory.Exists(directoryPath))
                    {
                        OpenFolderInExistingExplorer(directoryPath);
                    }
                    else
                    {
                        MessageBox.Show($"Directory does not exist:\n{directoryPath}", "Path Not Found", 
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Opens folder in existing explorer window
        private static void OpenFolderInExistingExplorer(string folderPath)
        {
            try
            {
                IntPtr pidl = ILCreateFromPathW(folderPath);
                if (pidl != IntPtr.Zero)
                {
                    try
                    {
                        SHOpenFolderAndSelectItems(pidl, 0, null, 0);
                    }
                    finally
                    {
                        ILFree(pidl);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());
                throw;
            }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr ILCreateFromPathW(string pszPath);

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[] apidl, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        // Windows API for window focus
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // Extracts directory from file path
        private static string GetDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = path.TrimEnd('\\', ' ');

            if (Path.HasExtension(path))
            {
                return Path.GetDirectoryName(path);
            }
            else
            {
                return path;
            }
        }

        // Checks if column contains path data
        public static bool IsPathColumn(DataGrid dataGrid)
        {
            if (dataGrid?.CurrentCell == null)
                return false;

            var column = dataGrid.CurrentCell.Column;
            if (column == null)
                return false;

            var headerText = column.Header?.ToString() ?? string.Empty;
            
            return headerText.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Gets current cell text value
        public static string GetCellValue(DataGrid dataGrid)
        {
            if (dataGrid?.CurrentCell == null)
                return null;

            try
            {
                var cellContent = dataGrid.CurrentCell.Column?.GetCellContent(dataGrid.CurrentCell.Item);
                if (cellContent is TextBlock textBlock)
                {
                    return textBlock.Text;
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
