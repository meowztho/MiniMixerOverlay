using System;
using System.Windows;
using System.Windows.Input;
using MiniMixerOverlay.Core;
using MiniMixerOverlay.UI.ViewModels;

namespace MiniMixerOverlay.UI
{
    public partial class MainWindow : Window
    {
        private readonly MixerController _controller;
        private readonly MainViewModel _viewModel;
        private bool _isDragging;
        private Point _dragStart;

        public MainWindow(MixerController controller)
        {
            _controller = controller;
            _viewModel = new MainViewModel(controller);
            DataContext = _viewModel;

            InitializeComponent();

            LoadPosition();
            _controller.Initialize();
            SavePosition();
        }

        // ═══ Drag ═══
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDragging = true;
                _dragStart = e.GetPosition(this);
                this.DragMove();
                _isDragging = false;
                SavePosition();
            }
        }

        // ═══ Buttons ═══
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _controller.Shutdown();
            Application.Current.Shutdown();
        }

        // ═══ Position ═══
        private void LoadPosition()
        {
            var w = _controller.Settings.Window;
            this.Left = w.X;
            this.Top = w.Y;
            this.Topmost = w.AlwaysOnTop;
        }

        private void SavePosition()
        {
            var w = _controller.Settings.Window;
            w.X = this.Left;
            w.Y = this.Top;
            _controller.SaveState();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (!_isDragging) SavePosition();
        }
    }
}
