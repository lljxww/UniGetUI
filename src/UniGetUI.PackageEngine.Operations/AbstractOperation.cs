using UniGetUI.Core.Logging;
using UniGetUI.Core.Tools;
using UniGetUI.PackageEngine.Enums;

namespace UniGetUI.PackageOperations;

public abstract class AbstractOperation
{
    public class OperationMetadata
    {
        /// <summary>
        /// Installation of X
        /// </summary>
        public string Title = "";

        /// <summary>
        /// X is being installed/upated/removed
        /// </summary>
        public string Status = "";

        /// <summary>
        /// X was installed
        /// </summary>
        public string SuccessTitle = "";

        /// <summary>
        /// X has been installed successfully
        /// </summary>
        public string SuccessMessage = "";

        /// <summary>
        /// X could not be installed.
        /// </summary>
        public string FailureTitle = "";

        /// <summary>
        /// X Could not be installed
        /// </summary>
        public string FailureMessage = "";

        /// <summary>
        /// Starting operation X with options Y
        /// </summary>
        public string OperationInformation = "";

        public readonly string Identifier;

        public OperationMetadata()
        {
            Identifier  =  new Random().NextInt64(1000000, 9999999).ToString();
        }
    }

    public readonly OperationMetadata Metadata = new();

    public readonly static List<AbstractOperation> OperationQueue = new();

    public event EventHandler<OperationStatus>? StatusChanged;
    public event EventHandler<EventArgs>? CancelRequested;
    public event EventHandler<(string, LineType)>? LogLineAdded;
    public event EventHandler<EventArgs>? OperationStarting;
    public event EventHandler<EventArgs>? OperationFinished;
    public event EventHandler<EventArgs>? Enqueued;
    public event EventHandler<EventArgs>? OperationSucceeded;
    public event EventHandler<EventArgs>? OperationFailed;

    public enum LineType
    {
        OperationInfo,
        Progress,
        StdOUT,
        StdERR
    }

    private List<(string, LineType)> LogList = new();
    private OperationStatus _status = OperationStatus.InQueue;
    public OperationStatus Status
    {
        get => _status;
        set { _status = value; StatusChanged?.Invoke(this, value); }
    }

    public bool Started { get; private set; }
    protected bool QUEUE_ENABLED;

    public AbstractOperation(bool queue_enabled)
    {
        QUEUE_ENABLED = queue_enabled;
        Status = OperationStatus.InQueue;
        Line("Please wait...", LineType.Progress);
    }

    public void Cancel()
    {
        switch (_status)
        {
            case OperationStatus.Canceled:
                break;
            case OperationStatus.Failed:
                break;
            case OperationStatus.Running:
                Status = OperationStatus.Canceled;
                CancelRequested?.Invoke(this, EventArgs.Empty);
                Status = OperationStatus.Canceled;
                break;
            case OperationStatus.InQueue:
                Status = OperationStatus.Canceled;
                OperationQueue.Remove(this);
                Status = OperationStatus.Canceled;
                break;
            case OperationStatus.Succeeded:
                break;
        }
    }

    protected void Line(string line, LineType type)
    {
        if(type != LineType.Progress) LogList.Add((line, type));
        LogLineAdded?.Invoke(this, (line, type));
    }

    public IReadOnlyList<(string, LineType)> GetOutput()
    {
        return LogList;
    }

    public async Task MainThread()
    {
        if (Metadata.Status == "") throw new InvalidDataException("Metadata.Status was not set!");
        if (Metadata.Title == "") throw new InvalidDataException("Metadata.Title was not set!");
        if (Metadata.OperationInformation == "") throw new InvalidDataException("Metadata.OperationInformation was not set!");
        if (Metadata.SuccessTitle == "") throw new InvalidDataException("Metadata.SuccessTitle was not set!");
        if (Metadata.SuccessMessage == "") throw new InvalidDataException("Metadata.SuccessMessage was not set!");
        if (Metadata.FailureTitle == "") throw new InvalidDataException("Metadata.FailureTitle was not set!");
        if (Metadata.FailureMessage == "") throw new InvalidDataException("Metadata.FailureMessage was not set!");

        Started = true;

        if (OperationQueue.Contains(this))
            throw new InvalidOperationException("This operation was already on the queue");

        Status = OperationStatus.InQueue;
        Line(Metadata.OperationInformation, LineType.OperationInfo);
        Line(Metadata.Status, LineType.Progress);

        // BEGIN QUEUE HANDLER
        if (QUEUE_ENABLED)
        {
            SKIP_QUEUE = false;
            OperationQueue.Add(this);
            Enqueued?.Invoke(this, EventArgs.Empty);
            int lastPos = -2;

            while (OperationQueue.First() != this && !SKIP_QUEUE)
            {
                int pos = OperationQueue.IndexOf(this);

                if (pos == -1) return;
                // In this case, operation was canceled;

                if (pos != lastPos)
                {
                    lastPos = pos;
                    Line(CoreTools.Translate("Operation on queue (position {0})...", pos), LineType.Progress);
                }
                await Task.Delay(100);
            }
        }
        // END QUEUE HANDLER

        // BEGIN ACTUAL OPERATION
        OperationVeredict result;
        Line(CoreTools.Translate("Starting operation..."), LineType.Progress);
        Status = OperationStatus.Running;
        OperationStarting?.Invoke(this, EventArgs.Empty);
        do
        {
            try
            {
                result = await PerformOperation();
            }
            catch (Exception e)
            {
                result = OperationVeredict.Failure;
                Logger.Error(e);
                foreach (string l in e.ToString().Split("\n")) Line(l, LineType.StdERR);
            }
        }
        while (result == OperationVeredict.AutoRetry);
        OperationQueue.Remove(this);
        // END OPERATION

        OperationFinished?.Invoke(this, EventArgs.Empty);
        if (result == OperationVeredict.Success)
        {
            Status = OperationStatus.Succeeded;
            OperationSucceeded?.Invoke(this, EventArgs.Empty);
            Line(Metadata.SuccessMessage, LineType.StdOUT);
        }
        else if (result == OperationVeredict.Failure)
        {
            Status = OperationStatus.Failed;
            OperationFailed?.Invoke(this, EventArgs.Empty);
            Line(Metadata.FailureMessage, LineType.StdERR);
            Line(Metadata.FailureMessage + " - " + CoreTools.Translate("Click here for more details"), LineType.Progress);
        }
        else if (result == OperationVeredict.Canceled)
        {
            Status = OperationStatus.Canceled;
            Line(CoreTools.Translate("Operation canceled by user"), LineType.StdERR);
        }
    }

    private bool SKIP_QUEUE;
    public void SkipQueue()
    {
        if (Status != OperationStatus.InQueue) return;
        OperationQueue.Remove(this);
        SKIP_QUEUE = true;
    }

    public void Retry()
    {
        if (Status is OperationStatus.Running or OperationStatus.InQueue) return;
        _ = MainThread();
    }

    protected abstract Task<OperationVeredict> PerformOperation();
    public abstract Task<Uri> GetOperationIcon();
}
