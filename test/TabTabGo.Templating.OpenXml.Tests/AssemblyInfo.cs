// Process-wide state (DotLiquid statics, the current directory some tests set) makes cross-class parallelism unsafe;
// the suite is small enough to run as one collection.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
