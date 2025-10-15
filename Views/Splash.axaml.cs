using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AndroidSideloader.Views
{
    public partial class Splash : Window
    {
        private Image _splashImage;

        public Splash()
        {
            InitializeComponent();

            // Find the image control
            _splashImage = this.FindControl<Image>("SplashImage");
        }

        /// <summary>
        /// Update the splash screen background image
        /// </summary>
        public void UpdateBackgroundImage(string imageName)
        {
            try
            {
                if (_splashImage != null)
                {
                    var uri = new Uri($"avares://AndroidSideloader/Resources/{imageName}");
                    _splashImage.Source = new Bitmap(AssetLoader.Open(uri));
                }
            }
            catch (Exception ex)
            {
                // Silently fail if image not found
                System.Diagnostics.Debug.WriteLine($"Failed to load splash image: {ex.Message}");
            }
        }
    }
}