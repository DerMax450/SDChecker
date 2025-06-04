using System.Configuration;
using System.Data;
using System.Windows;
using log4net;
using log4net.Config;
using System.IO;
using System.Reflection;

namespace SDChecker
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(App));


        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            XmlConfigurator.Configure(new FileInfo("log4net.config"));
            log.Info("Starting SDChecker Application... \n\n");
        }
    }
}
