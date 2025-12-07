
using System.IO.Hashing;
using System.Text;

namespace NoctesChat;

public class BloomFilter {
    private byte[] _filter;
    private ulong _capacityMask;
    private long _seed;

    // Check if capacity is a power of 2
    private bool isValidCapacity(int capacity) {
        if (capacity <= 0) return false;
        
        // This works because if capacity is a power of 2, the Most Significant Bit will be 1, with all other bits being 0.
        // When a power of 2 is subtracted by 1 it will set all other bits to 1.
        // Therefore, the AND bitwise should be 0
        return (capacity & (capacity - 1)) == 0;
    }
    
    public BloomFilter(long seed, int capacity = 1048576 /* 1 Mib */) {
        // same as capacity % 8 but optimized
        if ((capacity & 7) != 0) throw new Exception("Capacity must be divisible by 8");
        if (!isValidCapacity(capacity)) throw new Exception("Capacity must be a power of two");
        
        // same as capacity / 8 but optimized
        _filter = new byte[capacity >> 3];
        _capacityMask = (ulong)capacity - 1;
        _seed = seed;
    }

    public void Add(int rawBitPos) {
        // same as rawBitPos % 8 but optimized
        var bitPos = rawBitPos & 7;
        
        // same as rawBitPos / 8 but optimized
        var bytePos = rawBitPos >> 3;

        var bitMask = 1 << bitPos;
        
        _filter[bytePos] |= (byte)bitMask;
    }
    
    public bool Get(int rawBitPos) {
        // same as rawBitPos % 8 but optimized
        var bitPos = rawBitPos & 7;
        
        // same as rawBitPos / 8 but optimized
        var bytePos = rawBitPos >> 3;

        var bitMask = 1 << bitPos;
        
        return (_filter[bytePos] & bitMask) != 0;
    }

    public int ComputeHash(string input) {
        var hash = XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(input), _seed);
        
        // We can do & here because we checked that capacity is a Power of Two
        return (int)(hash & _capacityMask);
    }
}
