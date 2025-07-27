using System.IO;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Windows;
using System.Xml.Linq;
using CommandLine;
using Newtonsoft.Json;
using OWOGame;
using static OwoApp.OwoApp;

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
        private List<Sensation> sensations = new List<Sensation>
        { 
            new Sensation
            {
                Uuid = "test",
                Description = "Test sensation",
                Cost = "10",
                Prefix = "owo",
                Code = "0~Dart~12,1,30,0,0,0,Impact|5%10~impact-1~"
            }
        };
        private Dictionary<string, string> sensationIndexMap = new Dictionary<string, string>();
        private SocketIOClient.SocketIO client;
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // parse options
            ParseOptions();

            // start listening to a socket
            Console.WriteLine("Attemping to connect to SocketIO");
            await ConnectToSocketIO();

            // connect to owo
            Console.WriteLine("Attempting to connect to Owo");
            await ConnectToOwoVest();
            Console.WriteLine("Connected to Owo");
            await client.EmitAsync("owoConnected");
        }

        public class Options
        {
            [Option(shortName:'s', longName:"sensation-file", Required = false, HelpText = "Path to a file containing sensation options for the Owo Vest")]
            public string? SensationFile { get; set; }
        }

        private void ParseOptions()
        {
            string[] args = Environment.GetCommandLineArgs();
            Parser.Default.ParseArguments<Options>(args).WithParsed(ProcessOptions);
        }

        private void ProcessOptions(Options options)
        {
            Console.WriteLine("Options Found: " + options.ToString() + options.SensationFile);
            if (options.SensationFile != null)
            {
                sensations.AddRange(ParseSensations(options.SensationFile));
            }
        }

        public class Sensation
        {
            public string? Uuid { get; set; }

            public string? Description { get; set; }
            public string? Cost { get; set; }
            public string? Prefix { get; set; }
            [JsonProperty(PropertyName = "sensation")]
            public string? Code { get; set; }
        }

        private List<Sensation> ParseSensations(string path)
        {
            string? contents = null;
            try
            {
                contents = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error while processing sensation file (" + path + "): " + e.Message);
            }

            List<Sensation> sensations = new List<Sensation> { };

            if (!string.IsNullOrEmpty(contents))
            {
                List<Sensation>? list = JsonConvert.DeserializeObject<List<Sensation>>(contents);
                if (list != null && list.Count > 0)
                {
                    sensations.AddRange(list);
                }
            }
            return sensations;
            
        }

        public class SubmittedSensation
        {
            public SubmittedSensation(string code)
            {
                var parts = code.Split('~');
                var expectedLength = 5;
                if (parts.Length != expectedLength)
                {
                    throw new ArgumentException("Sensation code, \"" + code + "\", doesn't have the appropriate number of parameters. The parser expected " + expectedLength + " but received " + parts.Length + ".");
                }

                Index = parts[0];
                Name = parts[1];
                Code = new SensationCode(parts[2]);
                Icon = parts[3];
            }
            public string Index { get; set; }
            public string Name { get; set; }
            public SensationCode Code { get; set; }
            public string Icon { get; set; }

            public float TotalDuration()
            {
                return Code.TotalDuration();
            }

            public override string ToString()
            {
                var joinCharacter = '~';
                return Index + joinCharacter + Name + joinCharacter + Code + joinCharacter + Icon + joinCharacter;
            }
        }

        
        public class SensationCode
        {
            public SensationCode(string code)
            {
                var parts = code.Split('&');
                micros = parts.Select(micro => new MicroSensation(micro)).ToList();
            }

            public List<MicroSensation> micros {  get; set; }

            public float TotalDuration()
            {
                return micros.Select(micro => micro.TotalDuration()).Sum();
            }

            public override string ToString()
            {
                var joinCharacter = '&';
                return string.Join(joinCharacter, micros);
            }
        }
        
        public class MicroSensation
        {
            public MicroSensation(string microCode)
            {
                var parts = microCode.Split('|');
                var expectedParts = 2;
                if (parts.Length != expectedParts)
                {
                    throw new ArgumentException("Micro Sensation, \"" + microCode + "\", doesn't have the appropriate number of parameters. The parser expected " + expectedParts + " but received " + parts.Length + ".");
                }

                var metrics = parts[0].Trim().Split(',');
                var expectedMetrics = 7;
                if (metrics.Length != expectedMetrics)
                {
                    throw new ArgumentException("Micro Sensation Metrics, \"" + microCode + "\", doesn't have the appropriate number of parameters. The parser expected " + expectedMetrics + " but received " + parts.Length + ".");
                }
                frequency = metrics[0];
                duration = metrics[1];
                intensity = metrics[2];
                fadeIn = metrics[3];
                fadeOut = metrics[4];
                exitTime = metrics[5];
                name = metrics[6];

                shocks = parts[1].Trim().Split(',').ToList();
            }

            public string frequency { get; set; }
            public string duration { get; set; }
            public string intensity { get; set; }
            public string fadeIn { get; set; }
            public string fadeOut { get; set; }
            public string exitTime { get; set; }
            public string name { get; set; }

            public List<string> shocks { get; set; }

            public float TotalDuration()
            {
                return (float.Parse(duration) + float.Parse(exitTime)) / 10;
            }

            public override string ToString()
            {
                var partSplit = '|';
                var joinCharacter = ',';
                return frequency + joinCharacter + duration + joinCharacter + intensity + joinCharacter + fadeIn + joinCharacter + fadeOut + joinCharacter + exitTime + joinCharacter + name + partSplit + string.Join(joinCharacter, shocks);
            }
        }

        private Task ConnectToOwoVest()
        {
            var codes = new List<SubmittedSensation>();
            
            for (int i = 0; i < sensations.Count; i++)
            {
                var sensation = sensations[i];
                try
                {
                    var code = new SubmittedSensation(sensation.Code);
                    code.Index = i.ToString();

                    codes.Add(code);
                    sensationIndexMap.Add(sensation.Prefix + sensation.Cost, i.ToString());
                }
                catch (ArgumentException e)
                {
                    Console.WriteLine(e.Message);
                    client.EmitAsync("sensationParsingError", sensation.Uuid);
                }
            }

            var bakedSensations = codes.Select(code => BakedSensation.Parse(code.ToString())).ToArray();

            var auth = GameAuth.Create(bakedSensations);

            OWO.Configure(auth);

            return OWO.AutoConnect();
        }

        private Task SendTestSensation()
        {
            OWO.Send("0");
            return Task.Delay(100);
        }

        public class TriggerError
        {
            public string Type { get; set; }
            public string Uuid { get; set; }
            public string Message { get; set; }
        }

        private Task ConnectToSocketIO()
        {   
            var client = new SocketIOClient.SocketIO("http://localhost:3000");

            client.On("trigger", response =>
            {
                string keyword = response.GetValue<string>();
                string id = response.GetValue<string>(1);
                
                if (OWO.ConnectionState.Equals(ConnectionState.Connected))
                {
                    Console.WriteLine($"Shock Command Received: {keyword} {id}, connection: {OWO.ConnectionState.ToString()}");

                    if (sensationIndexMap.ContainsKey(keyword))
                    {

                        
                        string codeIndex = sensationIndexMap[keyword];
                        int index = int.Parse(codeIndex);
                        if (sensations != null)
                        {
                            var indexIsInRange = !(index < 0 || index >= sensations.Count);
                            if (indexIsInRange)
                            {
                                Sensation sensation = sensations[index];
                                if (sensation != null)
                                {
                                    
                                    SubmittedSensation parsedSensation = new SubmittedSensation(sensation.Code);
                                    var duration = parsedSensation.TotalDuration();

                                    Console.WriteLine("Shock Command Triggered (" + parsedSensation.Name + " " + duration.ToString("0.00") + "s)");

                                    OWO.Send(codeIndex);
                                    System.Threading.Thread.Sleep((int) (duration * 1000));

                                    client.EmitAsync("triggerResponse", id);
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Unknown Shock Command Received");
                        client.EmitAsync("triggerError", new TriggerError { Type = "Unrecognized", Uuid = id, Message = "The sensation requested was not found inside of sensations available." });
                    }
                } 
                else
                {
                    client.EmitAsync("tiggerError", new TriggerError { Type = "Disconnected", Uuid = id, Message = "The application is not connected to your Owo Vest." });
                }                
            });

            client.On("test", response =>
            {
                Console.WriteLine("Test Command Recieved");
                SendTestSensation().Wait();
            });

            client.On("stop", response =>
            {
                Console.WriteLine("Stop Received, Reason: " +  response);
                OWO.Disconnect();
                client.DisconnectAsync();
                Environment.Exit(0);
            });

            client.OnConnected += (s, e) => 
            {
                Console.WriteLine("Socket Connected");
            };

            this.client = client;

            return client.ConnectAsync();
            
        }
    }

}
