# TerraTechModLoader
 Manages dependencies and loads of TT unofficial and official mods

## Legacy API
```csharp
public class YourMod : ModBase {
	int LoadOrder;  // Default load order. In absence of dependencies, will serve as a "bucket" where all mods with similar LoadOrder are executed at once
	                // Load order of 0 means load immediately, higher load orders are loaded later
	Type[] LoadAfter(); // Define the type (ModBase) of mods that you want yours to be loaded after. Affects both EarlyInit & Init. DeInit is done in reverse order.
	Type[] LoadBefore(); // Define the type (ModBase) of mods that you want yours to be loaded before
	void ManagedEarlyInit(); // Use this if you want your EarlyInit behaviour to change based on if you're using 0ModManager or not
	...
}
```

## New API
```csharp
public class YourMod : ModBase {
	int InitOrder;  // Default load order for Init only
	int EarlyInitOrder;  // Default load order for EarlyInit only
	int UpdateOrder;  // Default load order for Update only
	int FixedUpdateOrder;  // Default load order for FixedUpdate only
	int LoadOrder; // Legacy parameter compatibility
	void ManagedEarlyInit();  // Legacy parameter compatibility
	IEnumerator<float> EarlyInitIterator();  // will take priority over whatever is specified in ManagedEarlyInit
	IEnumerator<float> InitIterator();  // Specify iterator for Init. (prevents freezing on loading screen)
	IEnumerator<float> DeInitIterator();  // Specify iterator for DeInit. (prevents freezing on loading screen)
	Type[] EarlyLoadAfter(); // Define the type (ModBase) of mods that you want yours to be loaded after during EarlyInit phase
	Type[] EarlyLoadBefore(); // Define the type (ModBase) of mods that you want yours to be loaded before during EarlyInit phase
	Type[] LoadAfter(); // Define the type (ModBase) of mods that you want yours to be loaded after. DeInit is done in reverse order
	Type[] LoadBefore(); // Define the type (ModBase) of mods that you want yours to be loaded before. DeInit is done in reverse order
	Type[] UpdateAfter();
	Type[] UpdateBefore();
	Type[] FixedUpdateAfter();
	Type[] FixedUpdateBefore();
	...
}
```
