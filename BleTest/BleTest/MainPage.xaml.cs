using Microsoft.Extensions.DependencyInjection;
using Shiny;
using Shiny.BluetoothLE.Hosting;
using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace BleTest
{
    public partial class MainPage : ContentPage
    {
        private readonly BleCentral bleCentral;
        public MainPage()
        {
            InitializeComponent();
            bleCentral = new BleCentral();
        }

        private void Button_Clicked(object sender, EventArgs e)
        {
            var started = bleCentral.ToggleServer();
            ToggleButton.Text = $"{(started ? "Stop" : "Start")} Server";
        }

        private void Button_Clicked_1(object sender, EventArgs e)
        {
            bleCentral.NotifyValue();   
        }
    }

    public class BleCentral
    {
        const string LocalName = "TestCentral";

        const string ServiceUuid = "00010000-F0F0-5555-AAAA-ADECAFC0FFEE";
        const string ReadWriteUuid = "00010001-F0F0-5555-AAAA-ADECAFC0FFEE";

        const string ReadOnlyUuid = "00010002-F0F0-5555-AAAA-ADECAFC0FFEE";
        const string NotifyUuid = "00010003-F0F0-5555-AAAA-ADECAFC0FFEE";
        const string WriteOnlyUuid = "00010004-F0F0-5555-AAAA-ADECAFC0FFEE";

        const string DeviceInformationUuid = "0000180a-0000-1000-8000-00805f9b34fb";
        const string SystemIdUuid = "00002a00-0000-1000-8000-00805f9b34fb";


        readonly IBleHostingManager hostingManager;

        private IGattService _service;
        private IGattCharacteristic _readWriteChar;
        private IGattCharacteristic _readOnlyChar;
        private IGattCharacteristic _notificationChar;

        public BleCentral()
        {
            hostingManager = ShinyHost.Resolve<IBleHostingManager>();
        }

        public bool ToggleServer()
        {
            if (hostingManager.IsAdvertising)
            {
                StopServer();
                return false;
            }
            _ = StartServer();
            return true;
        }

        public async Task StartServer()
        {
            try
            {
                if (hostingManager.IsAdvertising) return;

                hostingManager.ClearServices();
                

                _service =  await hostingManager.AddService(
                    ServiceUuid, true,
                    sb => {
                        _readWriteChar = sb.AddCharacteristic(
                            ReadWriteUuid,
                            cb =>
                            {
                                cb.SetWrite(ReadWrite_WriteRequest, WriteOptions.WriteWithoutResponse);
                                cb.SetRead(ReadWrite_ReadRequest);
                            }
                        );

                        _readOnlyChar = sb.AddCharacteristic(
                            ReadOnlyUuid,
                            cb =>
                            {
                                cb.SetRead(ReadOnly_ReadRequest);
                            }
                        );

                        _notificationChar = sb.AddCharacteristic(
                            NotifyUuid, cb =>
                            {
                                cb.SetNotification(OnSubscribe);
                            }
                        );

                        sb.AddCharacteristic(
                            WriteOnlyUuid, cb =>
                            {
                                cb.SetWrite(WriteOnly_WriteRequest, WriteOptions.WriteWithoutResponse);
                            }
                        );
                    }
                );

                await hostingManager.StartAdvertising(new AdvertisementOptions
                {
					LocalName = this.LocalName,
                    ServiceUuids = { ServiceUuid }
                });
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
                throw;
            }
        }

        IDisposable notifierSub;
        private void OnSubscribe(CharacteristicSubscription cs)
        {
            var cnt = cs.Characteristic.SubscribedCentrals.Count;
            Debug.Print($">>>>> {cnt} subscribers.");
            if (cnt == 0)
            {
                notifierSub?.Dispose();
                return;
            }

            this.notifierSub = Observable
                .Interval(TimeSpan.FromSeconds(2))
                .Select(_ => Observable.FromAsync(async () =>
                {
                    //NEVER GETS CALLED...?!??!
                    Debug.Print(">>>>> Notifying...");
                    var ticks = DateTime.Now.Ticks;
                    var data = BitConverter.GetBytes(ticks);
                    await _notificationChar.Notify(data);
                    return ticks;
                }))
                .Subscribe(x =>
                {
                    var ticks = DateTime.Now.Ticks;
                    var data = BitConverter.GetBytes(ticks);
                    _notificationChar.Notify(data);
                    Debug.Print(x.ToString());
                });
        }

        public void StopServer()
        {
            if (!hostingManager.IsAdvertising) return;
            hostingManager.ClearServices();
            this.hostingManager.StopAdvertising();
            //this.notifierSub?.Dispose();
        }

        private GattState WriteOnly_WriteRequest(WriteRequest request)
        {
            Debug.Print($">>>>> WRITEONLY REQUEST ACKNOWLEDGED ({request.Data.Length})");
            return GattState.Success;
        }

        private GattState ReadWrite_WriteRequest(WriteRequest request)
        {
            Debug.Print($">>>>> READWRITE WRITE REQUEST ACKNOWLEDGED ({request.Data.Length})");
            return GattState.Success;
        }

        private ReadResult ReadWrite_ReadRequest(ReadRequest request)
        {
            Debug.Print($">>>>> READWRITE READ REQUEST");

            var data = BitConverter.GetBytes(DateTime.Now.Ticks);
            return ReadResult.Success(data);
        }


        private ReadResult ReadOnly_ReadRequest(ReadRequest request)
        {
            Debug.Print($">>>>> INFO REQUEST ACKNOWLEDGED (Offset: {request.Offset})");

            var data = Encoding.UTF8.GetBytes("{\"deviceName\":\"TestDevice\",\"deviceId\":\"00000001-0001-0001-0001-000000000001\"}");
            return ReadResult.Success(data);
        }

        public void NotifyValue()
        {
            var data = Encoding.UTF8.GetBytes("1234");
            _ = _notificationChar.Notify(data);
        }
    }

    public class Startup : ShinyStartup
    {
        public override void ConfigureServices(IServiceCollection services, IPlatform platform)
        {
            services.UseBleHosting();
        }
    }
}
