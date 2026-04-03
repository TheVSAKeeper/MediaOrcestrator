using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Runner;

public partial class ToolsForm : Form
{
    private static readonly Color ColorNotInstalled = Color.FromArgb(255, 235, 235);
    private static readonly Color ColorUpdateAvailable = Color.FromArgb(255, 250, 220);
    private static readonly Color ColorUpToDate = Color.FromArgb(230, 255, 230);

    private readonly ToolManager _toolManager;
    private CancellationTokenSource? _cts;

    public ToolsForm(ToolManager toolManager)
    {
        _toolManager = toolManager;
        InitializeComponent();
        LoadCurrentState();
        Shown += async (_, _) => await CheckUpdatesAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnFormClosing(e);
    }

    private async void CheckUpdatesButton_Click(object? sender, EventArgs e)
    {
        await CheckUpdatesAsync();
    }

    private async void ToolsDataGridView_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.ColumnIndex != uiColAction.Index || e.RowIndex < 0)
        {
            return;
        }

        var toolName = uiToolsGrid.Rows[e.RowIndex].Cells[uiColName.Index].Value?.ToString();

        if (string.IsNullOrEmpty(toolName))
        {
            return;
        }

        var buttonCell = uiToolsGrid.Rows[e.RowIndex].Cells[uiColAction.Index];

        if (string.IsNullOrEmpty(buttonCell.Value?.ToString()))
        {
            return;
        }

        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        _cts?.Dispose();
        _cts = new();
        var token = _cts.Token;

        uiCheckUpdatesButton.Enabled = false;
        uiToolsGrid.Enabled = false;
        uiProgressBar.Visible = true;
        uiProgressBar.Value = 0;
        uiStatusLabel.Text = $"Обновление {toolName}... 0%";

        var progress = new Progress<double>(p =>
        {
            var percent = (int)(p * 100);
            uiProgressBar.Value = percent;
            uiStatusLabel.Text = $"Обновление {toolName}... {percent}%";
        });

        try
        {
            await Task.Run(() => _toolManager.UpdateToolAsync(toolName, progress, token), token);
            uiStatusLabel.Text = $"{toolName} успешно обновлён!";

            var statuses = await Task.Run(() => _toolManager.CheckForUpdatesAsync(token), token);
            RefreshGrid(statuses);
        }
        catch (OperationCanceledException)
        {
            uiStatusLabel.Text = $"Обновление {toolName} отменено";
        }
        catch (Exception ex)
        {
            uiStatusLabel.Text = $"Ошибка обновления {toolName}: {ex.Message}";
            MessageBox.Show($"Не удалось обновить {toolName}:\n\n{ex.Message}",
                "Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            uiProgressBar.Visible = false;
            uiCheckUpdatesButton.Enabled = true;
            uiToolsGrid.Enabled = true;
        }
    }

    private static void ApplyRowColor(DataGridViewRow row, Color color)
    {
        foreach (DataGridViewCell cell in row.Cells)
        {
            cell.Style.BackColor = color;
        }
    }

    private void LoadCurrentState()
    {
        uiToolsGrid.Rows.Clear();

        foreach (var (name, _) in _toolManager.GetRegistry())
        {
            var path = _toolManager.GetToolPath(name);
            var row = uiToolsGrid.Rows.Add(name,
                path is not null ? "..." : "—",
                "—",
                path is not null ? "Ожидание проверки" : "Не установлен",
                path is not null ? "Проверить" : "Установить");

            var gridRow = uiToolsGrid.Rows[row];
            var backColor = path is not null ? Color.White : ColorNotInstalled;
            ApplyRowColor(gridRow, backColor);

            if (path is not null)
            {
                gridRow.Cells[uiColName.Index].ToolTipText = path;
            }
        }
    }

    private async Task CheckUpdatesAsync()
    {
        uiCheckUpdatesButton.Enabled = false;
        uiStatusLabel.Text = "Проверка обновлений...";

        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        _cts?.Dispose();
        _cts = new();
        var token = _cts.Token;

        try
        {
            var statuses = await Task.Run(() => _toolManager.CheckForUpdatesAsync(token), token);
            token.ThrowIfCancellationRequested();
            RefreshGrid(statuses);
            uiStatusLabel.Text = $"Проверено: {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException)
        {
            uiStatusLabel.Text = "Проверка отменена";
        }
        catch (Exception ex)
        {
            uiStatusLabel.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            uiCheckUpdatesButton.Enabled = true;
        }
    }

    private void RefreshGrid(List<ToolStatus> statuses)
    {
        uiToolsGrid.Rows.Clear();

        foreach (var status in statuses)
        {
            var (statusText, actionText, backColor) = status switch
            {
                { InstalledVersion: null } => ("Не установлен", "Установить", ColorNotInstalled),
                { UpdateAvailable: true } => ("Есть обновление", "Обновить", ColorUpdateAvailable),
                _ => ("Актуально", "", ColorUpToDate),
            };

            var row = uiToolsGrid.Rows.Add(status.Name,
                status.InstalledVersion ?? "—",
                status.LatestVersion ?? "—",
                statusText,
                actionText);

            var gridRow = uiToolsGrid.Rows[row];
            ApplyRowColor(gridRow, backColor);

            if (status.ResolvedPath is not null)
            {
                gridRow.Cells[uiColName.Index].ToolTipText = status.ResolvedPath;
            }
        }
    }
}
