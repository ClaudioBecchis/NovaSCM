using System.Windows;

namespace PolarisManager;

public partial class DemoAI : Window
{
    public DemoAI()
    {
        InitializeComponent();
        GridResultsAI.ItemsSource = new[]
        {
            new AiRow("PC-LAB-001",    "Completato",    "100%", "WORKGROUP",           "07/03 09:42", "Postazione laboratorio A"),
            new AiRow("PC-LAB-002",    "In esecuzione", "44%",  "WORKGROUP",           "07/03 10:15", "Postazione laboratorio B"),
            new AiRow("PC-LAB-003",    "In attesa",     "0%",   "WORKGROUP",           "—",           "Postazione laboratorio C"),
            new AiRow("PC-UFFICIO-01", "In attesa",     "0%",   "corp.polariscore.it", "—",           "Postazione direzione"),
            new AiRow("PC-UFFICIO-02", "In attesa",     "0%",   "corp.polariscore.it", "—",           "Postazione segreteria"),
            new AiRow("SRV-LINUX-01",  "In esecuzione", "20%",  "WORKGROUP",           "07/03 10:10", "Server Ubuntu test"),
        };
    }
}

record AiRow(string Name, string Status, string Progress, string Domain, string LastSeen, string Notes);
