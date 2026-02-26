using HyperTool.Models;
using MahApps.Metro.Controls;
using System.Collections.Generic;
using System.Windows;

namespace HyperTool.Views;

public partial class HostNetworkWindow : MetroWindow
{
    public HostNetworkWindow(IReadOnlyList<HostNetworkAdapterInfo> adapters)
    {
        InitializeComponent();
        DataContext = adapters;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
