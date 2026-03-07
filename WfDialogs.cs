// Finestre di dialogo semplici per il tab Workflow
using System.Windows;
using System.Windows.Controls;
using WpfMedia  = System.Windows.Media;
using WpfColor  = System.Windows.Media.Color;
using WpfBrush  = System.Windows.Media.SolidColorBrush;

namespace PolarisManager;

/// <summary>Dialogo per inserire nome e descrizione di un workflow.</summary>
class WfNameWindow : Window
{
    public string WfNome { get; private set; } = "";
    public string WfDesc { get; private set; } = "";

    private readonly TextBox _nome;
    private readonly TextBox _desc;

    public WfNameWindow(string title, string nome, string desc)
    {
        Owner = Application.Current.MainWindow;
        Title = title;
        Width = 420; Height = 270;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new WpfBrush(WpfColor.FromRgb(0x0a, 0x0f, 0x1e));

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = title, FontSize = 15, FontWeight = FontWeights.Bold,
            Foreground = new WpfBrush(WpfColor.FromRgb(0x60, 0xa5, 0xfa)),
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(header, 0);

        var lblNome = MakeLabel("Nome *");
        Grid.SetRow(lblNome, 1);

        _nome = MakeTextBox(nome);
        Grid.SetRow(_nome, 2);

        var lblDesc = MakeLabel("Descrizione (opzionale)");
        Grid.SetRow(lblDesc, 3);

        _desc = MakeTextBox(desc);
        Grid.SetRow(_desc, 4);

        var spacer = new Border { Height = 8 };
        Grid.SetRow(spacer, 5);

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var btnCancel = MakeButton("Annulla", false);
        btnCancel.Click += (_, _) => { DialogResult = false; };
        var btnOk = MakeButton("OK", true);
        btnOk.Click += BtnOk_Click;
        btns.Children.Add(btnCancel);
        btns.Children.Add(btnOk);
        Grid.SetRow(btns, 6);

        root.Children.Add(header);
        root.Children.Add(lblNome);
        root.Children.Add(_nome);
        root.Children.Add(lblDesc);
        root.Children.Add(_desc);
        root.Children.Add(spacer);
        root.Children.Add(btns);
        Content = root;

        Loaded += (_, _) => _nome.Focus();
    }

    private void BtnOk_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nome.Text))
        {
            MessageBox.Show("Il nome è obbligatorio.", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Warning);
            _nome.Focus();
            return;
        }
        WfNome = _nome.Text.Trim();
        WfDesc = _desc.Text.Trim();
        DialogResult = true;
    }

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text, Foreground = new WpfBrush(WpfColor.FromRgb(0x94, 0xa3, 0xb8)),
        FontSize = 12, Margin = new Thickness(0, 6, 0, 3)
    };

    private static TextBox MakeTextBox(string text) => new()
    {
        Text = text,
        Background = new WpfBrush(WpfColor.FromRgb(0x0d, 0x1b, 0x3e)),
        Foreground = new WpfBrush(WpfColor.FromRgb(0xe2, 0xe8, 0xf0)),
        BorderBrush = new WpfBrush(WpfColor.FromRgb(0x1e, 0x3a, 0x5f)),
        Padding = new Thickness(8, 5, 8, 5), FontSize = 13
    };

    private static Button MakeButton(string text, bool primary) => new()
    {
        Content = text,
        Padding = new Thickness(20, 7, 20, 7),
        Margin  = new Thickness(primary ? 8 : 0, 0, 0, 0),
        Cursor  = System.Windows.Input.Cursors.Hand,
        Background  = new WpfBrush(primary
            ? WpfColor.FromRgb(0x05, 0x2e, 0x16)
            : WpfColor.FromRgb(0x1e, 0x2a, 0x40)),
        BorderBrush = new WpfBrush(primary
            ? WpfColor.FromRgb(0x16, 0xa3, 0x4a)
            : WpfColor.FromRgb(0x2a, 0x3a, 0x5f)),
        Foreground  = new WpfBrush(primary
            ? WpfColor.FromRgb(0x4a, 0xde, 0x80)
            : WpfColor.FromRgb(0x94, 0xa3, 0xb8)),
        BorderThickness = new Thickness(1),
    };
}

/// <summary>Dialogo per assegnare un workflow a un PC.</summary>
class WfAssignWindow : Window
{
    public string PcName     { get; private set; } = "";
    public int    WorkflowId { get; private set; }

    private readonly TextBox _pcName;
    private readonly ComboBox _wfCombo;

    public WfAssignWindow(List<WfRow> workflows)
    {
        Owner = Application.Current.MainWindow;
        Title = "Assegna Workflow a PC";
        Width = 420; Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new WpfBrush(WpfColor.FromRgb(0x0a, 0x0f, 0x1e));

        var root = new Grid { Margin = new Thickness(20) };
        for (int i = 0; i < 7; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = "Assegna Workflow a PC", FontSize = 15, FontWeight = FontWeights.Bold,
            Foreground = new WpfBrush(WpfColor.FromRgb(0x60, 0xa5, 0xfa)),
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(header, 0);

        var lblPc = MakeLabel("Nome PC *");
        Grid.SetRow(lblPc, 1);

        _pcName = MakeTextBox("");
        Grid.SetRow(_pcName, 2);

        var lblWf = MakeLabel("Workflow *");
        Grid.SetRow(lblWf, 3);

        _wfCombo = new ComboBox
        {
            Background  = new WpfBrush(WpfColor.FromRgb(0x0d, 0x1b, 0x3e)),
            Foreground  = new WpfBrush(WpfColor.FromRgb(0xe2, 0xe8, 0xf0)),
            BorderBrush = new WpfBrush(WpfColor.FromRgb(0x1e, 0x3a, 0x5f)),
            Padding = new Thickness(8, 5, 8, 5), FontSize = 13
        };
        foreach (var wf in workflows)
            _wfCombo.Items.Add(new ComboBoxItem { Content = wf.Nome, Tag = wf.Id });
        if (_wfCombo.Items.Count > 0) _wfCombo.SelectedIndex = 0;
        Grid.SetRow(_wfCombo, 4);

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var btnCancel = MakeButton("Annulla", false);
        btnCancel.Click += (_, _) => { DialogResult = false; };
        var btnOk = MakeButton("Assegna", true);
        btnOk.Click += BtnOk_Click;
        btns.Children.Add(btnCancel);
        btns.Children.Add(btnOk);
        Grid.SetRow(btns, 6);

        root.Children.Add(header);
        root.Children.Add(lblPc);
        root.Children.Add(_pcName);
        root.Children.Add(lblWf);
        root.Children.Add(_wfCombo);
        root.Children.Add(btns);
        Content = root;

        Loaded += (_, _) => _pcName.Focus();
    }

    private void BtnOk_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pcName.Text))
        {
            MessageBox.Show("Il nome PC è obbligatorio.", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Warning);
            _pcName.Focus();
            return;
        }
        if (_wfCombo.SelectedItem is not ComboBoxItem item)
        {
            MessageBox.Show("Seleziona un workflow.", "NovaSCM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        PcName     = _pcName.Text.Trim().ToUpperInvariant();
        WorkflowId = (int)(item.Tag ?? 0);
        DialogResult = true;
    }

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text, Foreground = new WpfBrush(WpfColor.FromRgb(0x94, 0xa3, 0xb8)),
        FontSize = 12, Margin = new Thickness(0, 6, 0, 3)
    };

    private static TextBox MakeTextBox(string text) => new()
    {
        Text = text,
        Background = new WpfBrush(WpfColor.FromRgb(0x0d, 0x1b, 0x3e)),
        Foreground = new WpfBrush(WpfColor.FromRgb(0xe2, 0xe8, 0xf0)),
        BorderBrush = new WpfBrush(WpfColor.FromRgb(0x1e, 0x3a, 0x5f)),
        Padding = new Thickness(8, 5, 8, 5), FontSize = 13
    };

    private static Button MakeButton(string text, bool primary) => new()
    {
        Content = text,
        Padding = new Thickness(20, 7, 20, 7),
        Margin  = new Thickness(primary ? 8 : 0, 0, 0, 0),
        Cursor  = System.Windows.Input.Cursors.Hand,
        Background  = new WpfBrush(primary
            ? WpfColor.FromRgb(0x05, 0x2e, 0x16)
            : WpfColor.FromRgb(0x1e, 0x2a, 0x40)),
        BorderBrush = new WpfBrush(primary
            ? WpfColor.FromRgb(0x16, 0xa3, 0x4a)
            : WpfColor.FromRgb(0x2a, 0x3a, 0x5f)),
        Foreground  = new WpfBrush(primary
            ? WpfColor.FromRgb(0x4a, 0xde, 0x80)
            : WpfColor.FromRgb(0x94, 0xa3, 0xb8)),
        BorderThickness = new Thickness(1),
    };
}
