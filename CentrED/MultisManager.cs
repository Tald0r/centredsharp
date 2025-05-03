using ClassicUO.Assets;
using System;
using System.Collections.Generic;
using System.Threading.Tasks; // Required for Task
using ClassicUO.IO; // Required for UOFileManager, UOFileIndex
using Microsoft.Xna.Framework.Graphics; // Required for GraphicsDevice

namespace CentrED;

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
    // Called from MapManager's Task.WhenAll
    public Task Load()
    {
        return Task.Run(() =>
        {
            try
            {
                // Access the singleton instance and call its Load method
                Loader?.Load(); // Call Load on the existing instance
                Console.WriteLine($"[INFO] MultisManager: ClassicUO.Assets.MultiLoader loaded {Loader?.File?.Length ?? 0} entries.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] MultisManager.Load task failed: {ex.Message}\n{ex.StackTrace}");
            }
        });
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
             Console.WriteLine("[WARN] MultisManager.Load(gd) called, but internal loader or its file is null (async load might have failed or not completed).");
        }
        else
        {
             Console.WriteLine($"[INFO] MultisManager.Load(gd) confirmed initialization. Accessing {Instance.Count} multi entries.");
        }
    }


    /// <summary>
    /// Gets the total number of multi entries available (valid or invalid).
    /// Uses the singleton loader instance.
    /// </summary>
    // Cast long to int for Count
    public int Count => (int)(Loader?.File?.Length ?? 0);

    /// <summary>
    /// Checks if a multi index is potentially valid (has an entry in the index file).
    /// Uses the singleton loader instance.
    /// </summary>
    public bool IsValidIndex(int index)
    {
        // Use File.Length for bounds check
        if (Loader?.File == null || index < 0 || index >= Loader.File.Length)
        {
            return false;
        }
        // Use GetValidRefEntry and check its properties
        ref readonly var entry = ref Loader.File.GetValidRefEntry(index);
        // Check if the entry lookup is valid or if it has a defined length
        return entry.Lookup != 0 || entry.Length > 0;
    }

    /// <summary>
    /// Retrieves the list of components for a given multi index using the ClassicUO loader.
    /// Returns null if the loader isn't available or the index is invalid.
    /// Can throw exceptions if reading the multi data fails.
    /// Uses the singleton loader instance.
    /// </summary>
    // Use fully qualified name ClassicUO.Assets.MultiInfo
    public List<ClassicUO.Assets.MultiInfo>? GetMultiComponents(int index)
    {
        if (Loader == null) // Check the singleton instance
        {
             Console.WriteLine($"[WARN] MultisManager.GetMultiComponents({index}): Loader instance is null.");
             return null;
        }
         if (!IsValidIndex(index)) // Use IsValidIndex for a preliminary check
        {
            return null;
        }
        try
        {
            // Call GetMultis on the singleton instance
            return Loader.GetMultis((uint)index);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] MultisManager.GetMultiComponents({index}): {ex.Message}");
            // Optionally log the stack trace: Console.WriteLine(ex.StackTrace);
            return null; // Return null on error
        }
    }

     /// <summary>
    /// Gets all valid multi IDs.
    /// Uses the singleton loader instance.
    /// </summary>
    public IEnumerable<int> GetAllValidIds()
    {
        // Use File.Length for iteration bounds
        if (Loader?.File == null)
        {
            yield break; // Return empty sequence if loader not ready
        }

        for (int i = 0; i < Loader.File.Length; i++)
        {
            if (IsValidIndex(i)) // Use the updated IsValidIndex check
            {
                yield return i;
            }
        }
    }
}