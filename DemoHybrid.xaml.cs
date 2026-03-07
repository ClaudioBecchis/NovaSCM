using System.Windows;

namespace PolarisManager;

public partial class DemoHybrid : Window
{
    public DemoHybrid()
    {
        InitializeComponent();
        GridResults.ItemsSource = new[]
        {
            new HybridRow(false, "PC-LAB-001",    "Completato",    "100%", "07/03 09:42",  "WORKGROUP",           "Deploy Base Win11"),
            new HybridRow(false, "PC-LAB-002",    "In esecuzione", "44%",  "07/03 10:15",  "WORKGROUP",           "Deploy Base Win11"),
            new HybridRow(false, "PC-LAB-003",    "In attesa",     "0%",   "—",            "WORKGROUP",           "Deploy Base Win11"),
            new HybridRow(false, "PC-UFFICIO-01", "In attesa",     "0%",   "—",            "corp.polariscore.it", "Deploy Base Win11"),
            new HybridRow(false, "PC-UFFICIO-02", "In attesa",     "0%",   "—",            "corp.polariscore.it", "Deploy Base Win11"),
            new HybridRow(false, "SRV-LINUX-01",  "In esecuzione", "20%",  "07/03 10:10",  "WORKGROUP",           "Setup Server Linux"),
        };
    }
}

record HybridRow(bool Sel, string Name, string Status, string Progress, string LastSeen, string Domain, string Workflow);
