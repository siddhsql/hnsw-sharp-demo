namespace HNSW.Net.Demo {
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Runtime.Intrinsics;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Threading;
    using System.Threading.Tasks;
    using HDF.PInvoke; 
    // https://github.com/HDFGroup/HDF.PInvoke/wiki/Important-Differences-between-HDF5-1.8-and-1.10
    // If you've hard-coded System.Int32 in places where an hid_t was asked for, you might be in trouble. A relatively safe 
    // method to deal with this change is 1) to use HDF5 API type names in your code, and 2) to begin every source file with a declaration similar to this:
    using hid_t = System.Int64;
    
    // https://github.com/HDFGroup/HDF.PInvoke/wiki/Cookbook
    class Hdf5Utils {

        public class ReadResult<T> {
            public ulong[] dimensions;
            public T[] data;

            public IList<T[]> ConvertTo2d() {
                if (this.dimensions.Length != 2) {
                    throw new Exception("this is not a a collection of 2D vectors");
                }
                int rows = (int) this.dimensions[0];
                int cols = this.data.Length / rows;
                T[][] result = new T[rows][];
                for(int i = 0; i < rows; i++) {
                    result[i] = new T[cols];
                    Array.Copy(this.data, i * cols, result[i], 0, cols); // memcopy
                }
                return result;
            }
        }

        static ulong[] Dimensions(long fileId, String dataset) {
            // Open the dataset
            long datasetId = H5D.open(fileId, dataset);

            // Get the datatype and dataspace
            long datatypeId = H5D.get_type(datasetId);
            long dataspaceId = H5D.get_space(datasetId);

            // Get the number of dimensions in the dataspace
            int rank = H5S.get_simple_extent_ndims(dataspaceId);

            // Get the dimensions of the dataspace
            ulong[] dimensions = new ulong[rank];
            H5S.get_simple_extent_dims(dataspaceId, dimensions, null);
            // Close the dataset, dataspace, and file
            H5D.close(datasetId);
            H5S.close(dataspaceId);

            return dimensions;
        }

        public static ReadResult<T> Read<T>(long fileId, String dataset) {
            // Open the dataset
            long datasetId = H5D.open(fileId, dataset);

            // Get the datatype and dataspace
            long datatypeId = H5D.get_type(datasetId);
            long dataspaceId = H5D.get_space(datasetId);

            // Get the number of dimensions in the dataspace
            int rank = H5S.get_simple_extent_ndims(dataspaceId);

            // Get the dimensions of the dataspace
            ulong[] dimensions = new ulong[rank];
            H5S.get_simple_extent_dims(dataspaceId, dimensions, null);

            // Read the data
            ulong n = 1;
            for (int i = 0; i < dimensions.Length; i++) {
                n *= dimensions[i];
            }

            T[] data = new T[n]; 
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            H5D.read(datasetId, datatypeId, H5S.ALL, H5S.ALL, H5P.DEFAULT, handle.AddrOfPinnedObject());
            handle.Free();

            // Close the dataset, dataspace, and file
            H5D.close(datasetId);
            H5S.close(dataspaceId);

            return new ReadResult<T> {
                dimensions = dimensions,
                data = data
            };
        }        

        public static IList<T[]> Read2DTensor<T>(long fileId, String dataset) {
            var x = Read<T>(fileId, dataset);
            return x.ConvertTo2d();
        }

        private static T[] flatten<T>(T[][] data) {
            int rows = data.Length;
            int cols = data[0].Length;
            int n = rows * cols;
            T[] result = new T[n];
            for (int i = 0; i < rows; i++) {
                Array.Copy(data[i], 0, result, i * cols, cols);
            }
            return result;
        }

        public static void Write2DTensor<T>(long fileId, String dataset, T[][] data) {
            var array = flatten<T>(data);
            string datasetName = "myDataset";
            ulong[] dimensions = { (ulong)data.Length, (ulong)data[0].Length }; // 2D array
            hid_t dataType = H5T.copy(GetDatatype(typeof(T))); 
            hid_t dataspaceId = H5S.create_simple(dimensions.Length, dimensions, null); // Create dataspace

            // Create the dataset
            hid_t datasetId = H5D.create(fileId, datasetName, dataType, dataspaceId, H5P.DEFAULT, H5P.DEFAULT, H5P.DEFAULT);
            unsafe {
                fixed (T* dataPtr = array) {
                    H5D.write(datasetId, dataType, H5S.ALL, H5S.ALL, H5P.DEFAULT, new IntPtr(dataPtr));
                }            
            }

            H5D.close(datasetId);
            H5S.close(dataspaceId);
            H5T.close(dataType); // https://github.com/HDFGroup/HDF.PInvoke/wiki/Cookbook-:-Strings
        }

        // from: https://github.com/SciSharp/HDF5-CSharp/blob/SciSharp.Keras.HDF5/HDF5-CSharp/Hdf5Common.cs#L105
        internal static hid_t GetDatatype(Type type)
        {
            hid_t dataType;

            var typeCode = Type.GetTypeCode(type);
            switch (typeCode)
            {
                case TypeCode.Byte:
                    dataType = H5T.NATIVE_UINT8;
                    break;
                case TypeCode.SByte:
                    dataType = H5T.NATIVE_INT8;
                    break;
                case TypeCode.Int16:
                    dataType = H5T.NATIVE_INT16;
                    break;
                case TypeCode.Int32:
                    dataType = H5T.NATIVE_INT32;
                    break;
                case TypeCode.Int64:
                    dataType = H5T.NATIVE_INT64;
                    break;
                case TypeCode.UInt16:
                    dataType = H5T.NATIVE_UINT16;
                    break;
                case TypeCode.UInt32:
                    dataType = H5T.NATIVE_UINT32;
                    break;
                case TypeCode.UInt64:
                    dataType = H5T.NATIVE_UINT64;
                    break;
                case TypeCode.Single:
                    dataType = H5T.NATIVE_FLOAT;
                    break;
                case TypeCode.Double:
                    dataType = H5T.NATIVE_DOUBLE;
                    break;
                //case TypeCode.DateTime:
                //    dataType = H5T.Native_t;
                //    break;
                case TypeCode.Char:
                    //dataType = H5T.NATIVE_UCHAR;
                    dataType = H5T.C_S1;
                    break;
                case TypeCode.String:
                    //dataType = H5T.NATIVE_UCHAR;
                    dataType = H5T.C_S1;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(type.Name, $"Data Type {type} not supported");
            }
            return dataType;
        }

        public static void WriteDataset<T>(long fileId, String dataset, T[] data, ulong[] dimensions) {
            string datasetName = dataset;
            
            hid_t typeId = H5T.copy(GetDatatype(typeof(T))); // https://github.com/HDFGroup/HDF.PInvoke/wiki/Cookbook-:-Strings
            hid_t dataspaceId = H5S.create_simple(dimensions.Length, dimensions, null); // Create dataspace
            // Create the dataset
            hid_t datasetId = H5D.create(fileId, datasetName, typeId, dataspaceId, H5P.DEFAULT, H5P.DEFAULT, H5P.DEFAULT);
            unsafe {
                fixed (T* dataPtr = data) {
                    H5D.write(datasetId, typeId, H5S.ALL, H5S.ALL, H5P.DEFAULT, new IntPtr(dataPtr));
                }
            }
            H5D.close(datasetId);
            H5S.close(dataspaceId);
            H5T.close(typeId); // https://github.com/HDFGroup/HDF.PInvoke/wiki/Cookbook-:-Strings
        }
    }
}