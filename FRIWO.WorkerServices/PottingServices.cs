using System.Device.Gpio;
using System.Text;
using System.IO.Ports;
using System.Linq;
using Microsoft.AspNetCore.Components;
using System.Text.Json;
using System.Threading;
using System.Text.RegularExpressions;
//using MarkingComponent;

namespace FRIWO.WorkerServices
{
    public class PottingServices : BackgroundService
    {

        int pinWorking = 17;
        int pinStart = 27;
        int pinStop = 5;
        int pinGetSerial = 22;

        int pinInterupt = 19;
        
        //Keypress listener
        DateTime? _lastKeystroke = new DateTime(0);
        List<char>? _barcode = new List<char>(50);

        SerialPort myport = new SerialPort();
        private readonly ILogger<PottingServices> _logger;

        private bool testing = false;
        HttpClient _httpClient;
        GpioController? controller;
        string[] serialList = new string[4]{"/dev/ttyUSB0",
                "/dev/ttyUSB1",
                "/dev/ttyUSB2",
                "/dev/ttyUSB3"};
        string pattern = @"^\d{8}-\d{7}-\d{7}-\d{3}$";
        public PottingServices(ILogger<PottingServices> logger)
        {
            _logger = logger;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _httpClient = new HttpClient();
            controller = new GpioController();
            return base.StartAsync(cancellationToken);
        }
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            return base.StopAsync(cancellationToken);
        }

        string portName = "";
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            portName = null;
            Console.WriteLine("Start open led pin");
            if (controller != null)
            {
                controller.OpenPin(pinWorking, PinMode.Output);
                controller.OpenPin(pinStart, PinMode.Output);
                controller.OpenPin(pinStop, PinMode.Output);
                controller.OpenPin(pinGetSerial, PinMode.Output);
                controller.OpenPin(pinInterupt, PinMode.Output);
                controller.Write(pinWorking, PinValue.Low);
                controller.Write(pinStart, PinValue.Low);
                controller.Write(pinStop, PinValue.Low);
                controller.Write(pinGetSerial, PinValue.Low);
                controller.Write(pinInterupt, PinValue.Low);
            }
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            try
            {
                PinValue pinValue = controller.Read(pinWorking);
                "Start watch".WriteLineColor(ConsoleColor.Green);
                SetupPort();
                myport.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                //controller.Write(pinWorking, PinValue.Low);
                controller.Write(pinStart, PinValue.Low);
                controller.Write(pinStop, PinValue.Low);
                controller.Write(pinGetSerial, PinValue.Low);
                myport.Close();
            }
            await Task.Delay(1000, stoppingToken);

        }

        //Receive and Send back data to potting machine
        void SetupPort()
        {
            if (string.IsNullOrEmpty(portName))
            {
                //test port connection
                foreach (var item in serialList)
                {
                    SerialPort testport = new SerialPort(item);
                    try
                    {
                        testport.Open();
                        Console.WriteLine(item);
                        if (testport.IsOpen == true)
                        {
                            portName = item;
                            testport.Close();
                            break;
                        }
                    }
                    catch (Exception es)
                    {
                        Console.WriteLine("" + es.Message);
                    }
                }
                //end test
            }
            myport = new SerialPort(portName, 9600);
            myport.ReadTimeout = 50000;
            myport.WriteTimeout = 50000;
            myport.NewLine = "\n";
            myport.RtsEnable = true;
            myport.ReceivedBytesThreshold = 1; //default is 1 
            myport.Open();
        }

        //Processing potting data
        async Task<Potting> ProcessingData(string data)
        {
            Potting potting = new Potting(); //No1: 123456: 123mils
            try{
                potting.Barcode = data.Split(":")[1].Split(":")[0].Replace(" ","");
                potting.PottingTime = DateTime.Now;
                potting.Status = "1";
                potting.Machine = "TWIN Potting 2 Nozzel" + data.Split(":")[0]??"";
                //var pottingSetting = await GetSettingPotting(potting.Barcode);
                PottingResult pottingRs = new PottingResult();
                pottingRs.Station = data.Split(":")[0]??"";
                pottingRs.LowerLimit = "0";
                pottingRs.Weight = data.Split(":")[2]??"0";
                pottingRs.UpperLimit = "0";
                potting.PottingResult = pottingRs;
                Console.WriteLine(data) ;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Processing data: "+ex.Message) ;
            }
            return potting;
        }

        //Send result data through api
        public async Task InsertData(Potting pottingData)
        {
            try
            {
                string? result = 
                                " -" + pottingData.PottingResult.Weight 
                                + "- ";
                var bodyJson = JsonSerializer.Serialize(pottingData);
                var rq = new HttpRequestMessage();
                rq.Method = HttpMethod.Post;
                //rq.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                var requestStr = $"http://fvn-s-web01.friwo.local:5000/api/ProcessLock/FA/InsertPottingDataAsync/{pottingData.Barcode}/0/{pottingData.Machine}/{result}";
                //$"http://fvn-s-web01.friwo.local:5000/api/ProcessLock/FA/InsertPottingAsync/{pottingData.Barcode}/{int.Parse(pottingData.Status)}/{pottingData.Machine}/{result}";
                rq.RequestUri = new Uri(requestStr);
                var rs = await _httpClient.SendAsync(rq);
               
                rs.EnsureSuccessStatusCode() ;
                Console.WriteLine(requestStr) ;
    var jsonResponse = await rs.Content.ReadAsStringAsync();
    Console.WriteLine($"{jsonResponse}\n");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine("Insert data: " + ex.Message);
            }
        }

        public async Task GetSettingPotting(string? partNo)
        {
            try
            {
                var rq = new HttpRequestMessage();
                rq.Method = HttpMethod.Get;
                rq.Content = new StringContent($"\"{partNo}\"", Encoding.UTF8, "application/json");
                var requestStr = $"http://fvn-s-web01.friwo.local:5000/api/ProcessLock";
                rq.RequestUri = new Uri(requestStr);
                var rs = await _httpClient.SendAsync(rq);
                var responseBody = await rs.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {    
                Thread.Sleep(100);  
                await Task.Run(async() => {          
                string tempReader = myport.ReadLine();
                Potting pottingData = new Potting();
                string reader = "";
                if (tempReader != "")
                {
                    reader = tempReader;
                    tempReader = "";
                    Console.WriteLine("Received: " + reader);
                    if(reader.Contains("Interrupt"))
                    {
                        Console.WriteLine("Send: Cycle down");
                        myport.WriteLine("Cycle down");
                    }                       
                    else if (reader.Contains("Resume"))
                    {
                        Console.WriteLine("Send: Cycle up");
                        myport.WriteLine("Cycle up");
                    }
                    else
                    {
                        Console.WriteLine("Send: OK");
                        myport.WriteLine("OK");
                    }    
                    switch (reader.Replace("\r",""))
                    {
                        case "Start":
                            controller.Write(pinStop, PinValue.Low);
                            controller.Write(pinStart, PinValue.High);
                            controller.Write(pinWorking, PinValue.High);
                            controller.Write(pinInterupt, PinValue.Low);
                            break;
                        case "Stop":
                            controller.Write(pinStop, PinValue.High);
                            controller.Write(pinWorking, PinValue.Low);
                            controller.Write(pinStart, PinValue.Low);
                            controller.Write(pinInterupt, PinValue.Low);
                            break;
                        case "Interrupt":
                            controller.Write(pinInterupt, PinValue.High);
                            controller.Write(pinStop, PinValue.Low);
                            controller.Write(pinWorking, PinValue.Low);
                            controller.Write(pinStart, PinValue.Low);
                            break;
                        case "Resume":
                            controller.Write(pinStop, PinValue.Low);
                            controller.Write(pinStart, PinValue.High);
                            controller.Write(pinWorking, PinValue.High);
                            controller.Write(pinInterupt, PinValue.Low);
                            break;
                    }
                    if (!reader.Contains("Start") && !reader.Contains("Stop") && !reader.Contains("Interrupt") && !reader.Contains("Resume"))
                    {
                        pottingData = await ProcessingData(reader);
                        Console.WriteLine(pottingData.Barcode);
                        Console.WriteLine(pottingData.Status);
                        Console.WriteLine(pottingData.Machine);
                        Console.WriteLine(pottingData.PottingResult.LowerLimit);
                                                Console.WriteLine(pottingData.PottingResult.UpperLimit);

                        if (pottingData != null && Regex.IsMatch(pottingData.Barcode, pattern))
                            await InsertData(pottingData);
                    }
                }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Data Received: "+ ex.Message);
            }
        }
    }
}