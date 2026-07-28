using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BitChat.Core.Interop.WinRT;

public sealed class BlePeripheral : IDisposable
{
    private BluetoothLEAdvertisementPublisher? _publisher;
    private GattServiceProvider? _serviceProvider;
    private GattLocalCharacteristic? _dataCharacteristic;

    private readonly Guid _serviceUuid;
    private readonly Guid _characteristicUuid;
    private readonly ushort _manufacturerId;

    public bool IsAdvertising => _publisher?.Status == BluetoothLEAdvertisementPublisherStatus.Started;

    public event Action<byte[]>? OnDataReceived;
    public event Action<string>? OnLog;

    public BlePeripheral(Guid serviceUuid, Guid characteristicUuid, ushort manufacturerId = 0xFFFF)
    { _serviceUuid = serviceUuid; _characteristicUuid = characteristicUuid; _manufacturerId = manufacturerId; }

    public void StartAdvertising(byte[] manufacturerData)
    {
        StopAdvertising();
        _publisher = new BluetoothLEAdvertisementPublisher();
        var mfrData = new BluetoothLEManufacturerData { CompanyId = _manufacturerId };
        var writer = new DataWriter(); writer.WriteBytes(manufacturerData);
        mfrData.Data = writer.DetachBuffer();
        _publisher.Advertisement.ManufacturerData.Add(mfrData);
        _publisher.Start();
        Log("BLE advertising started");
    }

    public void StopAdvertising() { _publisher?.Stop(); _publisher = null; }

    public async Task StartGattServiceAsync()
    {
        StopGattService();
        var result = await GattServiceProvider.CreateAsync(_serviceUuid);
        if (result.Error != BluetoothError.Success) { Log($"GATT service failed: {result.Error}"); return; }
        _serviceProvider = result.ServiceProvider;
        var charParams = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse | GattCharacteristicProperties.Notify,
            WriteProtectionLevel = GattProtectionLevel.Plain,
        };
        var charResult = await _serviceProvider.Service.CreateCharacteristicAsync(_characteristicUuid, charParams);
        if (charResult.Error != BluetoothError.Success) { Log($"Characteristic failed: {charResult.Error}"); return; }
        _dataCharacteristic = charResult.Characteristic;
        _dataCharacteristic.WriteRequested += (s, e) =>
        {
            var def = e.GetDeferral();
            try { var req = e.GetRequestAsync().AsTask().Result; var rdr = DataReader.FromBuffer(req.Value); var data = new byte[rdr.UnconsumedBufferLength]; rdr.ReadBytes(data); OnDataReceived?.Invoke(data); req.Respond(); }
            catch { try { e.GetRequestAsync().AsTask().Result.RespondWithProtocolError(0x06); } catch { } }
            def.Complete();
        };
        _serviceProvider.StartAdvertising();
        Log("GATT service advertising");
    }

    public void StopGattService() { _serviceProvider?.StopAdvertising(); _serviceProvider = null; _dataCharacteristic = null; }

    public async Task<bool> NotifyAsync(byte[] data)
    {
        if (_dataCharacteristic == null || _dataCharacteristic.SubscribedClients.Count == 0) return false;
        try { var w = new DataWriter(); w.WriteBytes(data); await _dataCharacteristic.NotifyValueAsync(w.DetachBuffer()); return true; }
        catch { return false; }
    }

    public bool HasSubscribers => _dataCharacteristic?.SubscribedClients.Count > 0;

    private void Log(string msg) => OnLog?.Invoke($"[Peripheral] {msg}");
    public void Dispose() { StopAdvertising(); StopGattService(); }
}

public sealed class BleScanner : IDisposable
{
    private BluetoothLEAdvertisementWatcher? _watcher;

    public bool IsScanning => _watcher?.Status == BluetoothLEAdvertisementWatcherStatus.Started;
    public event Action<ulong, short, byte[]>? OnAdvertisementReceived;
    public event Action<string>? OnLog;

    public BleScanner()
    {
        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        _watcher.Received += (s, e) =>
        {
            foreach (var mfr in e.Advertisement.ManufacturerData)
            {
                var reader = DataReader.FromBuffer(mfr.Data);
                var data = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(data);
                OnAdvertisementReceived?.Invoke(e.BluetoothAddress, e.RawSignalStrengthInDBm, data);
            }
        };
    }

    public void Start() { _watcher?.Start(); Log("WinRT scanner started"); }
    public void Stop() { _watcher?.Stop(); Log("WinRT scanner stopped"); }

    private void Log(string msg) => OnLog?.Invoke($"[Scanner] {msg}");
    public void Dispose() { _watcher?.Stop(); _watcher = null; }
}
