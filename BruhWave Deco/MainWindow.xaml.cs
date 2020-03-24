using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BruhWave_Deco
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = new MainViewModel();

        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;
            Title = "Bruh:Wave Deco";
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ViewModel.Stop();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsRunning)
                ViewModel.Stop();
            else
                ViewModel.Start();
        }

    }
}
