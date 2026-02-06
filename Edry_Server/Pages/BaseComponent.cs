using Microsoft.AspNetCore.Components;
using MSGS;
using System.Net.Sockets;
using System.Timers;

public class BaseComponent<TControl, TStatus> : ComponentBase , IDisposable
    where TControl : struct 
    where TStatus : struct
{
    protected int DeviceID; // ✅ Now passed as parameter
    protected TControl controlInstance;
    protected StructWrapper<TStatus>? statusWrapper;
    private System.Timers.Timer? refreshTimer;
    private readonly object _lock = new object();
    protected bool _isDisposed = false;

    public void InitializeComponent(int deviceID, TControl controlInstance, TStatus initialStatus)
    {
        this.DeviceID = deviceID;
        this.controlInstance = controlInstance;
        this.statusWrapper = new StructWrapper<TStatus>(initialStatus);
    }

    public BaseComponent()
    {
    }
    // ✅ Base constructor now requires `deviceID`
    //public BaseComponent(int deviceID, TControl controlInstance, TStatus initialStatus)
    //{
    //    //this.udpClient = udpClient;
    //    this.DeviceID = deviceID;
    //    this.controlInstance = controlInstance;// new TControl();
    //    this.statusWrapper = new StructWrapper<TStatus>(initialStatus);
    //}
    protected void HandleApplyUpdates(object updatedData)
    {
        if (updatedData is TControl control)
        {
            Console.WriteLine("ApplyUpdates Clicked! New UserData: {0}", updatedData);
            controlInstance = control;
            SonDispose();
            StateHasChanged();
        }
    }

    protected override void OnInitialized()
    {
        //MSGS.ClientManager.SendServerForwardCmdGeneric(DeviceID, 1, ref controlInstance);
        refreshTimer = new System.Timers.Timer(500);
        refreshTimer.Elapsed += (sender, args) => InvokeAsync(UpdateGUIValues);
        refreshTimer.Start();
    }

    protected virtual void UpdateGUIValuesLogic()
    {
        // Implemented in derived classes
    }

    protected virtual void SonDispose()
    {
        // Implemented in derived classes
    }

    private void UpdateGUIValues()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            UpdateGUIValuesLogic();
            InvokeAsync(StateHasChanged);
        }
    }
    public virtual void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return; // Prevent multiple disposals

            Console.WriteLine("BaseComponent is being destroyed!");
            
            refreshTimer?.Dispose(); // Cleanup resources
                                     
            _isDisposed = true; 
            GC.SuppressFinalize(this); // prevent unnecessary finalization
        }
    }
}
