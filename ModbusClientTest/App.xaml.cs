using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using log4net;
using log4net.Config;

// Configure log4net using the .log4net file
[assembly: log4net.Config.XmlConfigurator(ConfigFileExtension = "log4net", Watch = true)]
// This will cause log4net to look for a configuration file
// called [progam].exe.log4net in the application base directory
// The config file will be watched for changes.

namespace VVG.Modbus.ClientTest
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(App));

        static App()
        {
            _log.Info("Starting VVG.Modbus.Client test");
        }
    }
}
