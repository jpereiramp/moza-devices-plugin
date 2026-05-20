using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MozaDevicesPlugin.Devices;

namespace MozaDevicesPlugin.UI
{
    internal sealed class MozaSdkWheelDeviceControl : UserControl
    {
        private readonly MozaSdkWheelDeviceExtension _extension;
        private readonly DispatcherTimer _timer;
        private readonly TextBlock _title;
        private readonly TextBox _details;

        public MozaSdkWheelDeviceControl(MozaSdkWheelDeviceExtension extension)
        {
            _extension = extension;

            var root = new Grid
            {
                Margin = new Thickness(14)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _title = new TextBlock
            {
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(_title, 0);
            root.Children.Add(_title);

            _details = new TextBox
            {
                IsReadOnly = true,
                IsUndoEnabled = false,
                UndoLimit = 0,
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(_details, 1);
            root.Children.Add(_details);

            Content = root;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += (_, __) => Refresh();

            Loaded += (_, __) =>
            {
                Refresh();
                _timer.Start();
            };
            Unloaded += (_, __) => _timer.Stop();
        }

        private void Refresh()
        {
            string expected = string.IsNullOrWhiteSpace(_extension.ExpectedFriendlyName)
                ? _extension.ExpectedPrefix
                : _extension.ExpectedFriendlyName;

            SetTextIfChanged(_title, "MOZA SDK - " + (string.IsNullOrWhiteSpace(expected) ? "Wheel" : expected));
            SetTextIfChanged(_details, _extension.BuildDeviceStatusText());
        }

        private static void SetTextIfChanged(TextBlock textBlock, string text)
        {
            text = text ?? "";
            if (!string.Equals(textBlock.Text, text, StringComparison.Ordinal))
                textBlock.Text = text;
        }

        private static void SetTextIfChanged(TextBox textBox, string text)
        {
            text = text ?? "";
            if (!string.Equals(textBox.Text, text, StringComparison.Ordinal))
                textBox.Text = text;
        }
    }
}
