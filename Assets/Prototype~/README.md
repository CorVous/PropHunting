# Browser prototype scripts (not compiled)

The plain Unity C# runtime of the browser prototype, kept here as the reference for the UdonSharp port. Unity
ignores folders whose name ends in `~`, so none of this is imported or compiled. The map generator itself
(`Assets/Scripts/Gen`) is still compiled: the editor uses it to bake the generator's tables and to check the
Udon generator against it.
