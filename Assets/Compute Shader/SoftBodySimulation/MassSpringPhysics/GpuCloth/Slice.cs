using System.Runtime.InteropServices;

namespace SoftBodySimulation
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Slice
    {
        public readonly uint Start, End;

        /// <summary>
        /// Creates a new slice: [start, end[.
        /// </summary>
        /// <param name="start">The starting index of the slice, inclusive.</param>
        /// <param name="end">The ending index of the slice, non-inclusive.</param>
        public Slice(uint start, uint end)
        {
            Start = start;
            End = end;
        }
    }
}
