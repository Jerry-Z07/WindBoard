using Xunit;

// WPF/STA-bound tests need a shared single-threaded dispatcher; keep parallelization off unless UI threading constraints are addressed.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
