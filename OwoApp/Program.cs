using System.Windows;
using OWOGame;

namespace OwoApp
{
    public class EntryPoint
    {
        [STAThread]
        public static void Main()
        {
            Console.WriteLine("Starting Program");
            OwoApp app = new OwoApp();
            app.Run();
        }
    }
    public class OwoApp : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // start listening to a socket
            StartSocketManager().Wait();

            // connect to owo
            ConnectToOwoVest();
        }

        private Task ConnectToOwoVest()
        {
            var weakDart = "0~Dart~11,1,30,0,0,0,Impact|0%10,8%5~impact-1~Impacts";

            var auth = GameAuth.Parse(weakDart);

            OWO.Configure(auth);

            return OWO.AutoConnect();
        }

        private Task SendTestSensation()
        {
            OWO.Send("0");
            return Task.Delay(100);
        }

        private Task StartSocketManager()
        {   
            var client = new SocketIOClient.SocketIO("http://localhost:3000");

            client.On("shock", response =>
            {
                Console.WriteLine("Shock Command Recieved");
            });

            client.On("test", response =>
            {
                Console.WriteLine("Test Command Recieved");
                SendTestSensation().Wait();
            });

            client.On("stop", response =>
            {
                OWO.Disconnect();
                client.DisconnectAsync();
                Environment.Exit(0);
            });

            return client.ConnectAsync();
            
        }
    }

}
