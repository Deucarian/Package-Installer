using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGraphPanGesture
    {
        private const float DragThresholdSquared = 25f;
        private Vector2 start;
        private Vector2 previous;

        internal bool IsCandidate { get; private set; }
        internal bool HasMoved { get; private set; }
        internal int Button { get; private set; } = -1;

        internal void Begin(int button, Vector2 position)
        {
            IsCandidate = true;
            HasMoved = false;
            Button = button;
            start = previous = position;
        }

        internal Vector2 Move(Vector2 position)
        {
            if (!IsCandidate) return Vector2.zero;
            if (!HasMoved && (position - start).sqrMagnitude > DragThresholdSquared)
                HasMoved = true;
            Vector2 delta = HasMoved ? position - previous : Vector2.zero;
            previous = position;
            return delta;
        }

        internal void Reset()
        {
            IsCandidate = false;
            HasMoved = false;
            Button = -1;
        }
    }
}
