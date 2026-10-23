using Equibles.Media.HostedService.Models;

namespace Equibles.Media.HostedService;

/// <summary>
/// The run in flight and when it is saved. A folder becomes the run's position only once it is
/// fully processed, and positions are saved at most once per interval because each save is a
/// write on the store's own disk; phase changes and completion save at once.
/// </summary>
internal class BlobSweepProgress
{
    private readonly BlobSweepStateStore _store;
    private readonly BlobSweepState _state;
    private readonly TimeSpan _saveInterval;
    private DateTime _lastSave = DateTime.MinValue;

    public BlobSweepProgress(BlobSweepStateStore store, BlobSweepState state, TimeSpan saveInterval)
    {
        _store = store;
        _state = state;
        _saveInterval = saveInterval;
    }

    public BlobSweepRun Run => _state.Run;

    /// <summary>Records a fully processed folder (relative segments); the walk root is never a position.</summary>
    public void Finished(IReadOnlyList<string> folder)
    {
        if (folder.Count == 0)
        {
            return;
        }

        Run.Position = string.Join('/', folder);
        if (DateTime.UtcNow - _lastSave >= _saveInterval)
        {
            Save();
        }
    }

    public void StartPhase(BlobSweepPhase phase)
    {
        Run.Phase = phase;
        Run.Position = null;
        Save();
    }

    public void Complete()
    {
        _state.LastCompletedAt = DateTime.UtcNow;
        _state.LastSlot = Run.Slot;
        _state.Run = null;
        Save();
    }

    public void Save()
    {
        _store.Save(_state);
        _lastSave = DateTime.UtcNow;
    }
}
