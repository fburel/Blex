# Blex

## Why Blex?

Blex is a Xamarin Bluetooth plugin. It has been design to port native Bluetooth Low Energy (a.k.a Bluetooth 4.0) capacities of Android and iOS into the Xamarin cross platform world. The result is a Task based API  that leverage CoreBlueetooth for iOS and BluetoothAdapter for Android, supporting device back to Jellybeans or iOS 6.

Blex can work both with Xamarin Native project and Xamarin Form. It's main feature are :
* Scanning for nearby device(s), filtering based on the advertised data and the rssi level.
* Handling connection / disconnection event
* Gatt discovery
* Subscribtion to bluetooth notification on Gatt Characteristics
* Allowing encryption/decryption of the packets sent/received

Those features turn Blex into an ideal librairy to make companion app for IoT project.

## How do I install

Blex is ship as an official nugget package.

## How does it work?

First, you will need to create an instance of the BluetoothHandler. The methods change on iOS or Android, but both iOS and Android bluetooth handler implements the IBluetoothHandler interface so it is easy to create the instances in your platform specific project and access to them from everywhere with an IoC pattern (ServiceLocator or DependencyInjection)
In the following snippets, I use the service locator provided in the Splat librairie.

### Instaciating BluetoothHandler on Android

The android bluttoth handler requieres an AppContext object. I recommand placing the instanciation in the custom App class.

``` 
public class App : Application
{

    public App(IntPtr handle, JniHandleOwnership ownerShip) : base(handle, ownerShip)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();

        /* ... */

        Locator.CurrentMutable.RegisterLazySingleton(() => new BluetoothHandler(this), typeof(IBluetoothHandler));

    }

}
 ```

 ### Instaciating BluetoothHandler on iOS

Following the same idea in iOS, I recommand instanciating the BluetoothHandler in the AppDelegate's FinishLaunching method

``` 
public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
{
    // Override point for customization after application launch.
    // If not required for your application you can safely delete this method

    Window = new UIWindow(UIScreen.MainScreen.Bounds);

    /* ... */

    Locator.CurrentMutable.Register(() => new BluetoothHandler(), typeof(IBluetoothHandler));
    
    return true;
}
 ```

### Scanning for a device (cross-platform)

Scanning is performed using the following API:

```
  Task<IEnumerable<IScanResult>> Scan(int limit, TimeSpan maxDuration, Predicate<IScanResult> filter,
            string[] uuidsRequiered);
```
  
The following snippet looks up for a device exposing a given service UUID and return the first device seen.

```
protected async Task<IScanResult> ScanFirst(string seekedUUID, int rssiLimit, int timeout) 
{

    var scanResult = await BluetoothHandler.Scan(1, RSSILimit: rssiLimit, maxDuration: timeout, uuidsRequiered: new[] {seekedUUID});

    if (!scanResult.Any())
        throw new Exception("no device matching criteria");

    var d = scanResult.OrderBy(x => x.RSSI).Reverse().First();

    return d;
}
```

### Connecting to a device (cross-platform)

Once a IScanResult object is obtain, The following API handle the connection / disconnection and Connection status change:
  
```
#region Connection

/// <summary>
/// A boolean value indicating if an active BLE connection currently exist
/// </summary>
bool IsConnected { get; }

/// <summary>
/// Connect to a given device
/// </summary>
/// <param name="scanResult"> The device to connect to</param>
/// <returns></returns>
Task Connect(IScanResult scanResult);

/// <summary>
/// Disconnect for the currently connected device
/// </summary>
/// <returns></returns>
Task Disconnect();

/// <summary>
/// An event raised when the connection status changed.
/// </summary>
/// <note>
/// As observed when the bluetooth device disconnect itself:
/// on iOS the event is raised instantly
/// on event is raise after a delay up to 20 seconds 
/// </note>
event EventHandler<bool> ConnectionStatusChanged;

#endregion

```

The following snippet uses the ScanFirst snippet written above to looks for a device (for up to 10 seconds) and connect to it

```
 protected async Task<bool> Connect()
{
    var uuid = "173ac77c-0235-11eb-adc1-0242ac120002";

    try
    {
        Console.WriteLine("Scanning for devices");
                
        var foundDevice = await ScanFirst(uuid, -100, 10 * 1000);

        await BluetoothHandler.Connect(device);
    }
    finally
    {
        return BluetoothHandler.IsConnected;
    }
}
```

### Discovering GATT (cross-platform)

The following APIs allows for gatt discovery 

```
#region GATT Discovery

/// <summary>
/// This methods discovers the GATT services and characteristic of the connected device
/// </summary>
/// <returns></returns>
Task DiscoverGATT();

/// <summary>
/// Return wether a given characteristic has been discovered
/// </summary>
/// <param name="characteristic"></param>
/// <returns></returns>
bool HasDiscoveredCharacteristics(string characteristic);

#endregion

```
  
The following uses the Connect snippet written above and once a connection is established, discover the gatt. 

```
protected async Task Start() 
{

    var connected = await Connect();

    if (connected)
    {
        await BluetoothHandler.DiscoverGatt();
    }
    else
    {
        // no device found or connection failed... It happens sometimes! You can restart the process (maybe up to 3 times)
    }
}
```

### Writing to a characteristic (cross-platform)

The following APIs allows for gatt discovery 

```
#region GATT Discovery

/// <summary>
/// This methods discovers the GATT services and characteristic of the connected device
/// </summary>
/// <returns></returns>
Task DiscoverGATT();

/// <summary>
/// Return wether a given characteristic has been discovered
/// </summary>
/// <param name="characteristic"></param>
/// <returns></returns>
bool HasDiscoveredCharacteristics(string characteristic);

#endregion

```
  
The following uses the Connect snippet written above and once a connection is established, discover the gatt. 

```
protected async Task Start() 
{

    var connected = await Connect();

    if (connected)
    {
        await BluetoothHandler.DiscoverGatt();
    }
    else
    {
        // no device found or connection failed... It happens sometimes! You can restart the process (maybe up to 3 times)
    }
}
```