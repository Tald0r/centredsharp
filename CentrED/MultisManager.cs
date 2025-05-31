using ClassicUO.Assets;
using System;
using System.Collections.Generic;
using System.Threading.Tasks; // Required for Task
using ClassicUO.IO; // Required for UOFileIndex, UOFile, StackDataReader
using Microsoft.Xna.Framework.Graphics; // Required for GraphicsDevice
using System.Runtime.InteropServices; // Required for StructLayout
using System.Runtime.CompilerServices; // Required for Unsafe

namespace CentrED;

// Define struct to hold component info
public struct MultiComponent
{
    public ushort ID;
    public short X;
    public short Y;
    public short Z;
    public bool IsVisible; // Simplified flag based on parsing logic
}

public class MultisManager
{
    // Instance field and property matching HuesManager
    private static MultisManager _instance;
    public static MultisManager Instance => _instance;

    // Reference to the singleton instance of the ClassicUO loader
    private MultiLoader? Loader => MultiLoader.Instance;

    // Private constructor for singleton pattern, called by static Load
    private MultisManager() { }

    // Instance Load method returns a Task and performs the async loading
    // It calls the Load method of the MultiLoader singleton instance.
    public Task Load()
    {
        // Directly return the task from the underlying loader
        return Loader?.Load() ?? Task.CompletedTask;
    }

    // Static Load method matching the HuesManager pattern
    // Called after the async loading is complete.
    // Creates the singleton instance.
    public static void Load(GraphicsDevice gd)
    {
        // Create the singleton instance here, like HuesManager
        _instance = new MultisManager();

        // Optional: Log confirmation after instance creation
        if (Instance.Loader == null || Instance.Loader.File == null)
        {
             Console.WriteLine("[WARN] MultisManager.Load(gd) called, but ClassicUO.Assets.MultiLoader instance or its File is null.");
        }
        else
        {
             Console.WriteLine($"[INFO] MultisManager.Load(gd) confirmed initialization. Accessing {Instance.Count} multi entries.");
        }
    }

    /// <summary>
    /// Gets the total number of multi entries available.
    /// Uses the singleton loader instance's Count property.
    /// </summary>
    public int Count => Loader?.Count ?? 0;

    /// <summary>
    /// Retrieves the list of components for a given multi index.
    /// Uses the UOFile provided by MultiLoader.Instance to read and parse the specific data block.
    /// Assumes MUL format only.
    /// Returns null if the loader isn't available or the index is invalid/causes an error during read.
    /// Returns an empty list for valid but empty entries.
    /// </summary>
    public unsafe List<MultiComponent>? GetMultiComponents(int index)
    {
        // Check if Loader and its File are available
        if (Loader?.File == null)
        {
             Console.WriteLine($"[WARN] MultisManager.GetMultiComponents({index}): Loader instance or its File is null.");
             return null;
        }
        // Bounds check against Loader.Count
         if (index < 0 || index >= Loader.Count)
        {
             Console.WriteLine($"[WARN] MultisManager.GetMultiComponents({index}): Index out of bounds (0-{Loader.Count - 1}).");
            return null; // Index is outside the known range
        }

        var list = new List<MultiComponent>();
        try
        {
            // Use the File object provided by MultiLoader.Instance
            var file = Loader.File;
            // Get the "pointer" (index entry) for this multi ID
            ref readonly var entry = ref file.GetValidRefEntry(index);

            // If entry lookup failed or length is zero, it's an empty/invalid multi
            if (entry.Lookup == 0 || entry.Length <= 0)
            {
                // Console.WriteLine($"[DEBUG] MultisManager.GetMultiComponents({index}): Entry has Lookup {entry.Lookup}, Length {entry.Length}. Returning empty list.");
                return list;
            }

            // *** Use the UOFile (Loader.File) to read the specific data block ***
            // This is necessary to get the component data based on the entry "pointer"
            file.Seek(entry.Offset, System.IO.SeekOrigin.Begin);
            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent((int)entry.Length);
            try
            {
                // Read the data using the file handle from MultiLoader
                file.Read(buffer, 0, (int)entry.Length);
                var reader = new StackDataReader(buffer.AsSpan(0, (int)entry.Length));

                // Simplified parsing: Assume MUL format only
                var blockSize = sizeof(MultiBlock);
                if (blockSize == 0)
                {
                    Console.WriteLine($"[ERROR] MultisManager.GetMultiComponents({index}): MultiBlock size is zero.");
                    return null;
                }
                var componentCount = entry.Length / blockSize;

                for (var i = 0; i < componentCount; ++i)
                {
                    var block = reader.Read<MultiBlock>();
                    list.Add(new MultiComponent
                    {
                        ID = block.ID,
                        X = block.X,
                        Y = block.Y,
                        Z = block.Z,
                        IsVisible = block.Flags != 0 // Standard MUL visibility check
                    });
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex) // Catch potential exceptions during file access or parsing
        {
            Console.WriteLine($"[ERROR] MultisManager.GetMultiComponents({index}): {ex.Message}");
            return null; // Return null on error
        }
        return list;
    }

     /// <summary>
    /// Gets all valid multi IDs by iterating up to the Count provided by MultiLoader.
    /// </summary>
    public IEnumerable<int> GetAllValidIds()
    {
        if (Loader == null) // Check if loader itself is available
        {
            yield break;
        }
        // Iterate from 0 up to the Count provided by MultiLoader
        for (int i = 0; i < Loader.Count; i++)
        {
            // Yield all indices in the range. GetMultiComponents will handle if they are truly empty/invalid.
            yield return i;
        }
    }
}

// Define the structs needed for parsing, based on MultiLoader source
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public ref struct MultiBlock
{
    public ushort ID;
    public short X;
    public short Y;
    public short Z;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public ref struct MultiBlockNew // Not used for parsing in MUL-only mode
{
    public ushort ID;
    public short X;
    public short Y;
    public short Z;
    public ushort Flags;
    public uint Unknown;
}