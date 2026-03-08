// NovaSCM v1.5.0
using System.Windows;
using System.Windows.Controls;

namespace PolarisManager;

public partial class WorkflowStepWindow : Window
{
    public WfStepRow? Result { get; private set; }

    private static readonly Dictionary<string, string> DefaultParams = new()
    {
        ["winget_install"]   = "{\"id\":\"\"}",
        ["windows_update"]   = "{\"category\":\"all\",\"exclude_drivers\":false,\"reboot_after\":false}",
        ["apt_install"]      = "{\"package\":\"\"}",
        ["snap_install"]     = "{\"package\":\"\",\"classic\":false}",
        ["ps_script"]        = "{\"script\":\"\"}",
        ["shell_script"]     = "{\"script\":\"\"}",
        ["reg_set"]          = "{\"path\":\"\",\"name\":\"\",\"value\":\"\",\"type\":\"REG_SZ\"}",
        ["file_copy"]        = "{\"src\":\"\",\"dst\":\"\"}",
        ["systemd_service"]  = "{\"name\":\"\",\"action\":\"start\"}",
        ["reboot"]           = "{\"delay\":5}",
        ["message"]          = "{\"text\":\"\"}",
    };

    private static readonly Dictionary<string, string> Helpers = new()
    {
        ["winget_install"]   = "{ \"id\": \"Mozilla.Firefox\" }  — ID winget del pacchetto",
        ["windows_update"]   = "category: \"all\" | \"security\" | \"critical\"\nexclude_drivers: true/false\nreboot_after: true/false",
        ["apt_install"]      = "{ \"package\": \"nginx\" }  — nome pacchetto apt",
        ["snap_install"]     = "{ \"package\": \"code\", \"classic\": true }",
        ["ps_script"]        = "{ \"script\": \"Write-Output 'Hello'\" }  — script PowerShell inline",
        ["shell_script"]     = "{ \"script\": \"echo hello\" }  — script bash/cmd inline",
        ["reg_set"]          = "path: HKLM\\SOFTWARE\\...  name: chiave  value: valore  type: REG_SZ|REG_DWORD",
        ["file_copy"]        = "{ \"src\": \"C:\\temp\\file.txt\", \"dst\": \"C:\\dest\\file.txt\" }",
        ["systemd_service"]  = "{ \"name\": \"nginx\", \"action\": \"start\" }  — azioni: start|stop|restart|enable",
        ["reboot"]           = "{ \"delay\": 5 }  — secondi prima del riavvio",
        ["message"]          = "{ \"text\": \"Messaggio visualizzato nel log\" }",
    };

    public WorkflowStepWindow(WfStepRow? existing, int defaultOrdine)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;

        if (existing != null)
        {
            TxtTitle.Text     = "Modifica Step";
            TxtNome.Text      = existing.Nome;
            TxtOrdine.Text    = existing.Ordine.ToString();
            TxtParametri.Text = existing.Parametri;
            SelectComboByTag(CmbTipo,      existing.Tipo);
            SelectComboByTag(CmbPlatform,  existing.Platform);
            SelectComboByTag(CmbSuErrore,  existing.SuErrore);
        }
        else
        {
            TxtOrdine.Text = defaultOrdine.ToString();
            // Imposta defaults per winget_install (già selezionato)
            TxtParametri.Text = DefaultParams["winget_install"];
            TxtHelper.Text    = Helpers["winget_install"];
        }
    }

    private static void SelectComboByTag(ComboBox cmb, string tag)
    {
        foreach (ComboBoxItem item in cmb.Items)
            if (item.Tag?.ToString() == tag) { cmb.SelectedItem = item; return; }
    }

    private void CmbTipo_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (CmbTipo.SelectedItem is not ComboBoxItem item) return;
        var tipo = item.Tag?.ToString() ?? "";
        if (DefaultParams.TryGetValue(tipo, out var def))
            TxtParametri.Text = def;
        if (Helpers.TryGetValue(tipo, out var help) && TxtHelper != null)
            TxtHelper.Text = help;
    }

    private void BtnOk_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtNome.Text))
        {
            MessageBox.Show("Il nome dello step è obbligatorio.", "NovaSCM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtNome.Focus();
            return;
        }
        if (!int.TryParse(TxtOrdine.Text, out var ordine) || ordine < 1)
        {
            MessageBox.Show("L'ordine deve essere un numero intero positivo.", "NovaSCM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtOrdine.Focus();
            return;
        }
        // Valida JSON parametri
        var parametri = TxtParametri.Text.Trim();
        try { System.Text.Json.JsonDocument.Parse(parametri); }
        catch
        {
            MessageBox.Show("I parametri non sono JSON valido.", "NovaSCM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtParametri.Focus();
            return;
        }

        var tipo      = (CmbTipo.SelectedItem     as ComboBoxItem)?.Tag?.ToString() ?? "message";
        var platform  = (CmbPlatform.SelectedItem  as ComboBoxItem)?.Tag?.ToString() ?? "all";
        var suErrore  = (CmbSuErrore.SelectedItem  as ComboBoxItem)?.Tag?.ToString() ?? "stop";

        Result = new WfStepRow
        {
            Ordine    = ordine,
            Nome      = TxtNome.Text.Trim(),
            Tipo      = tipo,
            Parametri = parametri,
            Platform  = platform,
            SuErrore  = suErrore,
        };
        DialogResult = true;
    }

    private void BtnCancel_Click(object s, RoutedEventArgs e)
        => DialogResult = false;
}
