namespace HNSW.Net.Demo
{
    using hid_t = System.Int64;
    
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.CompilerServices;
    using System.Runtime.Intrinsics;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Threading;
    using System.Threading.Tasks;
    using HDF.PInvoke;

    using Parameters = SmallWorld<float[], float>.Parameters;
    using System.Runtime.InteropServices;
    
    public static class Program
    {        
        public static void Main()
        {
            const int K = 10;
            string fileName = @"/Users/siddjain/github/ann-benchmarks/data/glove-100-angular.hdf5";
            string outFileName = @"/Users/siddjain/github/hnsw-sharp/Src/HNSW.Net.Demo/output.hdf5";
            H5.open();
            long fileId = H5F.open(fileName, H5F.ACC_RDONLY);
            var train = Hdf5Utils.Read2DTensor<float>(fileId, "train");
            var test = Hdf5Utils.Read2DTensor<float>(fileId, "test");
            H5F.close(fileId);

            var world = new SmallWorld<float[], float>(CosineDistance.SIMDForUnits, 
                                        DefaultRandomGenerator.Instance, 
                                        new Parameters() {
                                            M = 16,
                                            ConstructionPruning = 100,  // efConstruction
                                            EnableDistanceCacheForConstruction  = true,
                                            InitialDistanceCacheSize = 1024 * 1024,
                                            NeighbourHeuristic = NeighbourSelectionHeuristic.SelectHeuristic, 
                                            KeepPrunedConnections = true,
                                            ExpandBestSelection = true
                                        }, 
                                        threadSafe : true); // threadSafe: true means multiple threads will be calling world.AddItems 
                                                            // (or other methods) at the same time
            Console.WriteLine("Building spatial index...");
            Stopwatch sw = new Stopwatch();
            sw.Start();
            world.AddItems(train.Take(100).ToList());   // taking 100 just for testing correctness of the code.
            sw.Stop();
            var elapsedMs = sw.ElapsedMilliseconds;
            Console.WriteLine("Time: {0:0.00} ms", elapsedMs);

            Console.WriteLine("Querying index...");
            sw.Reset();
            sw.Start();
            int n = test.Count();
            int m = test[0].Length;
            var knn = new List<HNSW.Net.SmallWorld<float[],float>.KNNSearchResult>[n];
            for (int i = 0; i < n; i++) {
                var vec = test[i];
                knn[i] = world.KNNSearch(vec, K).ToList();
            }
            sw.Stop();
            elapsedMs = sw.ElapsedMilliseconds;
            Console.WriteLine("Time: {0:0.00} ms", elapsedMs);

            Console.WriteLine("Saving query results...");
            // we have to munge the data into correct form first
            var labels = new int[n*m];
            var distances = new float[n*m];
            for (int i = 0, ctr = 0; i < n; i++, ctr++) {                
                IList<HNSW.Net.SmallWorld<float[],float>.KNNSearchResult> x = knn[i];
                for (int j = 0; j < K; j++) {
                    HNSW.Net.SmallWorld<float[],float>.KNNSearchResult match = x[j];
                    labels[ctr] = match.Id;
                    distances[ctr] = match.Distance;    // distances are coming out as -ve
                }
            }

            // now we can save to HDF5
            hid_t outFileId = H5F.create(outFileName, H5F.ACC_TRUNC);
            ulong[] dimensions = new ulong[] {(ulong)n, (ulong)m};
            // 'The type initializer for 'HDF.PInvoke.H5T' threw an exception.'
            // Innermost exception 	 System.IO.FileNotFoundException : libhdf5.dylib            
            Hdf5Utils.WriteDataset<int>(outFileId, "labels", labels, dimensions);
            Hdf5Utils.WriteDataset<float>(outFileId, "distances", distances, dimensions);
            H5F.close(outFileId);
            H5.close();
        }
    }
}
