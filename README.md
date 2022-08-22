# TerraTechModLoader
 Manages dependencies and loads of TT unofficial and official mods

## Legacy API
```csharp
public class YourMod : ModBase {
	static int LoadOrder;  // Default load order. In absence of dependencies, will serve as a "bucket" where all mods with similar LoadOrder are executed at once
	                // Load order of 0 means load immediately, higher load orders are loaded later
	static Type[] LoadAfter(); // Define the type (ModBase) of mods that you want yours to be loaded after. Affects both EarlyInit & Init. DeInit is done in reverse order.
	static Type[] LoadBefore(); // Define the type (ModBase) of mods that you want yours to be loaded before
	void ManagedEarlyInit(); // Use this if you want your EarlyInit behaviour to change based on if you're using 0ModManager or not
	...
}
```

## New API
```csharp
public class YourMod : ModBase {
	static int InitOrder;  // Default load order for Init only
	static int EarlyInitOrder;  // Default load order for EarlyInit only
	static int UpdateOrder;  // Default load order for Update only
	static int FixedUpdateOrder;  // Default load order for FixedUpdate only
	static int LoadOrder; // Legacy parameter compatibility
	void ManagedEarlyInit();  // Legacy parameter compatibility
	IEnumerator<float> EarlyInitIterator();  // will take priority over whatever is specified in ManagedEarlyInit
	IEnumerator<float> InitIterator();  // Specify iterator for Init. (prevents freezing on loading screen)
	IEnumerator<float> DeInitIterator();  // Specify iterator for DeInit. (prevents freezing on loading screen)
	static Type[] EarlyLoadAfter(); // Define the type (ModBase) of mods that you want yours to be loaded after during EarlyInit phase
	static Type[] EarlyLoadBefore(); // Define the type (ModBase) of mods that you want yours to be loaded before during EarlyInit phase
	static Type[] LoadAfter(); // Define the type (ModBase) of mods that you want yours to be loaded after. DeInit is done in reverse order
	static Type[] LoadBefore(); // Define the type (ModBase) of mods that you want yours to be loaded before. DeInit is done in reverse order
	static Type[] UpdateAfter();
	static Type[] UpdateBefore();
	static Type[] FixedUpdateAfter();
	static Type[] FixedUpdateBefore();
	...
}
```
