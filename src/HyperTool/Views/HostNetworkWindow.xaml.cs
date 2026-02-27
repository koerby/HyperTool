using HyperTool.Helpers;
using HyperTool.Models;
using MahApps.Metro.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace HyperTool.Views;

public partial class HostNetworkWindow : MetroWindow
{
    public IReadOnlyList<HostNetworkAdapterInfo> Adapters { get; }

    public string AdapterCountText => $"{Adapters.Count} Adapter gefunden";

    public HostNetworkWindow(IReadOnlyList<HostNetworkAdapterInfo> adapters)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DwmWindowHelper.ApplyRoundedCorners(this);
        Adapters = adapters
            .Where(adapter => adapter is not null)
            .ToList();
        DataContext = this;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
