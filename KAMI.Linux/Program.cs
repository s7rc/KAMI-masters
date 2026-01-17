using Gtk;
using System;

namespace KAMI.Linux
{
    class Program
    {
        static int Main(string[] args)
        {
            var app = Application.New("org.kami.linux", Gio.ApplicationFlags.FlagsNone);
            
            app.OnActivate += (sender, e) =>
            {
                var window = new MainWindow();
                window.Application = app;
                window.Show();
            };

            return app.RunWithSynchronizationContext(null);
        }
    }
}
