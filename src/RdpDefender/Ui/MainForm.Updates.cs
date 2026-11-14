using RdpDefender.Infrastructure;

namespace RdpDefender.Ui;

public sealed partial class MainForm
{
    private async void UpdateButtonOnClick(object? sender, EventArgs e)
    {
        if (_updateBusy) return;

        if (_pendingUpdate is not null)
        {
            await InstallUpdateAsync();
            return;
        }

        await CheckUpdatesAsync();
    }

    private async Task CheckUpdatesAsync()
    {
        _updateBusy = true;
        _btnUpdate.Enabled = false;
        _btnUpdate.Text = "Проверка…";
        _svcUpdateStatus.Text = "Проверка GitHub Releases…";

        try
        {
            var update = await UpdateService.CheckAsync(UpdateService.CurrentVersion, CancellationToken.None);
            if (update is null)
            {
                _svcUpdateStatus.Text = $"Установлена последняя версия {UpdateService.CurrentVersion}.";
                _btnUpdate.Text = "Проверить снова";
            }
            else
            {
                _pendingUpdate = update;
                _svcUpdateStatus.Text = $"Доступна версия {update.Version}.";
                _btnUpdate.Text = $"Установить {update.Version}";

                var notes = string.IsNullOrWhiteSpace(update.Notes)
                    ? ""
                    : "\n\n" + Truncate(update.Notes, 1200);
                UiDialog.Info(this, "Доступно обновление",
                    $"Текущая версия: {UpdateService.CurrentVersion}\n" +
                    $"Новая версия: {update.Version}\n\n" +
                    "Будет скачан установщик с GitHub и проверен по SHA-256." + notes);
            }
        }
        catch (Exception ex)
        {
            _svcUpdateStatus.Text = "Не удалось проверить обновления.";
            _btnUpdate.Text = "Проверить снова";
            UiDialog.Error(this, "Обновление", "Не удалось проверить обновления:\n" + ex.Message);
        }
        finally
        {
            _updateBusy = false;
            _btnUpdate.Enabled = true;
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is null) return;

        _updateBusy = true;
        _btnUpdate.Enabled = false;
        _btnUpdate.Text = "Скачивание…";
        _svcUpdateStatus.Text = "Скачивание установщика…";

        try
        {
            var progress = new Progress<int>(value =>
            {
                _svcUpdateStatus.Text = value >= 100
                    ? "Проверка SHA-256…"
                    : $"Скачивание установщика… {value}%";
            });

            var path = await UpdateService.DownloadAndVerifyAsync(
                _pendingUpdate, progress, CancellationToken.None);

            _svcUpdateStatus.Text = "Запуск установщика…";
            UpdateService.LaunchInstaller(path);
            _svcUpdateStatus.Text = "Установщик запущен. Следуйте его инструкциям.";
            _btnUpdate.Text = "Проверить обновления";
            _pendingUpdate = null;
        }
        catch (Exception ex)
        {
            _svcUpdateStatus.Text = "Обновление не установлено.";
            _btnUpdate.Text = "Повторить установку";
            UiDialog.Error(this, "Обновление", "Не удалось скачать или запустить обновление:\n" + ex.Message);
        }
        finally
        {
            _updateBusy = false;
            _btnUpdate.Enabled = true;
        }
    }

    private static string Truncate(string text, int max)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}
