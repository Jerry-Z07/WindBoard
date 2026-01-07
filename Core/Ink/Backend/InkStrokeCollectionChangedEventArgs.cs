using System;
using System.Collections.Generic;
using WindBoard.Models.Ink;

namespace WindBoard.Core.Ink.Backend
{
    internal sealed class InkStrokeCollectionChangedEventArgs : EventArgs
    {
        public InkStrokeCollectionChangedEventArgs(IReadOnlyList<InkStrokeModel> added, IReadOnlyList<InkStrokeModel> removed)
        {
            Added = added ?? Array.Empty<InkStrokeModel>();
            Removed = removed ?? Array.Empty<InkStrokeModel>();
        }

        public IReadOnlyList<InkStrokeModel> Added { get; }

        public IReadOnlyList<InkStrokeModel> Removed { get; }
    }
}

