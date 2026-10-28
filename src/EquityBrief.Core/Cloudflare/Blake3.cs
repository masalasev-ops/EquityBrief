using System.Buffers.Binary;
using System.Numerics;

namespace EquityBrief.Core.Cloudflare;

// BLAKE3's 32-byte hash, written from the function's own reference implementation, since Cloudflare Pages names each
// uploaded file by the BLAKE3 hash of its contents in base64 followed by its extension and the framework carries none.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens and holds no control
public static class Blake3
{
    const int BlockLength = 64;
    const int ChunkLength = 1024;
    const uint ChunkStart = 1;
    const uint ChunkEnd = 2;
    const uint Parent = 4;
    const uint Root = 8;

    static readonly uint[] Iv = [0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A, 0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19];

    static readonly int[] Permutation = [2, 6, 3, 10, 7, 0, 4, 13, 1, 11, 12, 5, 9, 14, 15, 8];

    public static byte[] Hash(ReadOnlySpan<byte> input)
    {
        var stack = new List<uint[]>();
        var chunk = new Chunk(Iv, 0);

        while (input.Length > 0)
        {
            if (chunk.Length == ChunkLength)
            {
                var chunkValue = chunk.Output().ChainingValue();
                var total = chunk.Counter + 1;

                while ((total & 1) == 0)
                {
                    chunkValue = ParentOutput(stack[^1], chunkValue).ChainingValue();
                    stack.RemoveAt(stack.Count - 1);
                    total >>= 1;
                }

                stack.Add(chunkValue);
                chunk = new Chunk(Iv, chunk.Counter + 1);
            }

            var take = Math.Min(ChunkLength - chunk.Length, input.Length);

            chunk.Update(input[..take]);
            input = input[take..];
        }

        var output = chunk.Output();

        for (var remaining = stack.Count - 1; remaining >= 0; remaining--)
        {
            output = ParentOutput(stack[remaining], output.ChainingValue());
        }

        var words = Compress(output.InputValue, output.Block, 0, output.BlockLength, output.Flags | Root);
        var hash = new byte[32];

        for (var word = 0; word < 8; word++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(hash.AsSpan(word * 4), words[word]);
        }

        return hash;
    }

    static Output ParentOutput(uint[] left, uint[] right) => new(Iv, [.. left, .. right], 0, BlockLength, Parent);

    sealed record Output(uint[] InputValue, uint[] Block, ulong Counter, uint BlockLength, uint Flags)
    {
        public uint[] ChainingValue() => Compress(InputValue, Block, Counter, BlockLength, Flags)[..8];
    }

    sealed class Chunk(uint[] key, ulong counter)
    {
        uint[] value = key;
        readonly byte[] block = new byte[BlockLength];
        int blockLength;
        int blocksCompressed;

        public ulong Counter { get; } = counter;

        public int Length => (BlockLength * blocksCompressed) + blockLength;

        uint StartFlag => blocksCompressed == 0 ? ChunkStart : 0;

        public void Update(ReadOnlySpan<byte> input)
        {
            while (input.Length > 0)
            {
                if (blockLength == BlockLength)
                {
                    value = Compress(value, Words(block), Counter, BlockLength, StartFlag)[..8];
                    blocksCompressed++;
                    Array.Clear(block);
                    blockLength = 0;
                }

                var take = Math.Min(BlockLength - blockLength, input.Length);

                input[..take].CopyTo(block.AsSpan(blockLength));
                blockLength += take;
                input = input[take..];
            }
        }

        public Output Output() => new(value, Words(block), Counter, (uint)blockLength, StartFlag | ChunkEnd);
    }

    static uint[] Words(byte[] block)
    {
        var words = new uint[16];

        for (var word = 0; word < 16; word++)
        {
            words[word] = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(word * 4));
        }

        return words;
    }

    static uint[] Compress(uint[] chaining, uint[] block, ulong counter, uint blockLength, uint flags)
    {
        uint[] state =
        [
            chaining[0], chaining[1], chaining[2], chaining[3], chaining[4], chaining[5], chaining[6], chaining[7],
            Iv[0], Iv[1], Iv[2], Iv[3], (uint)counter, (uint)(counter >> 32), blockLength, flags,
        ];

        var message = (uint[])block.Clone();

        for (var round = 0; round < 7; round++)
        {
            Round(state, message);

            if (round < 6)
            {
                message = [.. Permutation.Select(at => message[at])];
            }
        }

        for (var word = 0; word < 8; word++)
        {
            state[word] ^= state[word + 8];
            state[word + 8] ^= chaining[word];
        }

        return state;
    }

    static void Round(uint[] state, uint[] message)
    {
        Mix(state, 0, 4, 8, 12, message[0], message[1]);
        Mix(state, 1, 5, 9, 13, message[2], message[3]);
        Mix(state, 2, 6, 10, 14, message[4], message[5]);
        Mix(state, 3, 7, 11, 15, message[6], message[7]);
        Mix(state, 0, 5, 10, 15, message[8], message[9]);
        Mix(state, 1, 6, 11, 12, message[10], message[11]);
        Mix(state, 2, 7, 8, 13, message[12], message[13]);
        Mix(state, 3, 4, 9, 14, message[14], message[15]);
    }

    static void Mix(uint[] state, int a, int b, int c, int d, uint x, uint y)
    {
        state[a] = state[a] + state[b] + x;
        state[d] = BitOperations.RotateRight(state[d] ^ state[a], 16);
        state[c] = state[c] + state[d];
        state[b] = BitOperations.RotateRight(state[b] ^ state[c], 12);
        state[a] = state[a] + state[b] + y;
        state[d] = BitOperations.RotateRight(state[d] ^ state[a], 8);
        state[c] = state[c] + state[d];
        state[b] = BitOperations.RotateRight(state[b] ^ state[c], 7);
    }
}
