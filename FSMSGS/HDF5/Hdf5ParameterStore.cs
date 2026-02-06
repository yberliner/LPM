using System;
using System.Runtime.InteropServices;
using HDF.PInvoke;

public class Hdf5FloatGrowingDataset : IDisposable
{
    private long _fileId = -1;

    public Hdf5FloatGrowingDataset(string filePath, bool createNew = false)
    {
        if (File.Exists(filePath))
        {
            _fileId = H5F.open(filePath, H5F.ACC_RDWR);
        }
        else
        {
            _fileId = H5F.create(filePath, H5F.ACC_TRUNC);
        }

        //if (createNew)
        //    _fileId = H5F.create(filePath, H5F.ACC_TRUNC);
        //else
        //    _fileId = H5F.open(filePath, H5F.ACC_RDWR);

        if (_fileId < 0)
            throw new Exception("Failed to open/create HDF5 file.");
    }

    public float[] ReadAllValues(string datasetName)
    {
        if (H5L.exists(_fileId, datasetName) <= 0)
            throw new Exception($"Dataset '{datasetName}' does not exist.");

        long datasetId = H5D.open(_fileId, datasetName);
        long dataspaceId = H5D.get_space(datasetId);

        ulong[] dims = new ulong[1];
        H5S.get_simple_extent_dims(dataspaceId, dims, null);

        int numElements = (int)dims[0];
        float[] result = new float[numElements];

        GCHandle handle = GCHandle.Alloc(result, GCHandleType.Pinned);
        try
        {
            H5D.read(datasetId, H5T.IEEE_F32LE, H5S.ALL, H5S.ALL, H5P.DEFAULT, handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }

        H5S.close(dataspaceId);
        H5D.close(datasetId);

        return result;
    }
    /// <summary>
    /// default is float (we always use this one except linux time)
    /// </summary>
    /// <param name="datasetName"></param>
    /// <param name="value"></param>
    //public void AddValue(string datasetName, object value)
    //{
    //    AddValue(datasetName, value, H5T.IEEE_F32LE);
    //}

    //public void AddValue(string datasetName, List<object> values)
    //{
    //    AddValue(datasetName, values, H5T.IEEE_F32LE);
    //}

    public void AddValues((string FatherName, string DatasetName) key, List<object> values, long hdf5Type)
    {
        if (values == null || values.Count == 0)
            return;
        
        string fatherName = key.FatherName;
        string datasetName = key.DatasetName;

        // Normalize types
        if (hdf5Type == H5T.NATIVE_LONG)
            hdf5Type = H5T.STD_I64LE;

        // Combine full dataset path
        string fullPath = $"{fatherName}/{datasetName}";

        // Ensure group exists
        if (H5L.exists(_fileId, fatherName) <= 0)
        {
            long groupId = H5G.create(_fileId, fatherName);
            H5G.close(groupId);
        }

        long datasetId;
        long dataspaceId;
        ulong[] dims = new ulong[1];
        ulong currentSize = 0;
        ulong newSize;

        int elementSize;
        Func<object, byte[]> convert;

        if (hdf5Type == H5T.IEEE_F32LE)
        {
            elementSize = sizeof(float);
            convert = v => BitConverter.GetBytes(Convert.ToSingle(v));
        }
        else if (hdf5Type == H5T.STD_I64LE)
        {
            elementSize = sizeof(long);
            convert = v => BitConverter.GetBytes(Convert.ToInt64(v));
        }
        else
        {
            throw new ArgumentException("Unsupported HDF5 type");
        }

        int count = values.Count;
        byte[] buffer = new byte[elementSize * count];
        for (int i = 0; i < count; i++)
        {
            byte[] bytes = convert(values[i]);
            Buffer.BlockCopy(bytes, 0, buffer, i * elementSize, elementSize);
        }

        if (H5L.exists(_fileId, fullPath) > 0)
        {
            datasetId = H5D.open(_fileId, fullPath);
            dataspaceId = H5D.get_space(datasetId);
            H5S.get_simple_extent_dims(dataspaceId, dims, null);
            H5S.close(dataspaceId);

            currentSize = dims[0];
            newSize = currentSize + (ulong)count;

            H5D.set_extent(datasetId, new ulong[] { newSize });
        }
        else
        {
            ulong[] maxDims = { H5S.UNLIMITED };
            ulong[] chunkDims = { 1024 };

            dataspaceId = H5S.create_simple(1, new ulong[] { 0 }, maxDims);
            long propId = H5P.create(H5P.DATASET_CREATE);
            H5P.set_chunk(propId, 1, chunkDims);

            // Add this line to enable deflate (zlib) compression, level 6 (0-9)
            H5P.set_deflate(propId, 6);


            datasetId = H5D.create(_fileId, fullPath, hdf5Type, dataspaceId, H5P.DEFAULT, propId);

            H5P.close(propId);
            H5S.close(dataspaceId);

            currentSize = 0;
            newSize = (ulong)count;

            H5D.set_extent(datasetId, new ulong[] { newSize });
        }

        dataspaceId = H5D.get_space(datasetId);
        H5S.select_hyperslab(dataspaceId, H5S.seloper_t.SET,
            new ulong[] { currentSize }, null, new ulong[] { (ulong)count }, null);

        long memspaceId = H5S.create_simple(1, new ulong[] { (ulong)count }, null);

        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            H5D.write(datasetId, hdf5Type, memspaceId, dataspaceId, H5P.DEFAULT, handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }

        H5S.close(dataspaceId);
        H5S.close(memspaceId);
        H5D.close(datasetId);
    }


    //public void AddValues(string datasetName, List<object> values, long hdf5Type)
    //{
    //    if (values == null || values.Count == 0)
    //        return;

    //    // Normalize platform-dependent types to platform-independent
    //    if (hdf5Type == H5T.NATIVE_LONG)
    //    {
    //        hdf5Type = H5T.STD_I64LE;
    //    }

    //    long datasetId;
    //    long dataspaceId;
    //    ulong[] dims = new ulong[1];
    //    ulong currentSize = 0;
    //    ulong newSize;

    //    int elementSize;
    //    Func<object, byte[]> convert;

    //    if (hdf5Type == H5T.IEEE_F32LE)
    //    {
    //        elementSize = sizeof(float);
    //        convert = v => BitConverter.GetBytes(Convert.ToSingle(v));
    //    }
    //    else if (hdf5Type == H5T.STD_I64LE)
    //    {
    //        elementSize = sizeof(long);
    //        convert = v => BitConverter.GetBytes(Convert.ToInt64(v));
    //    }
    //    else
    //    {
    //        throw new ArgumentException("Unsupported HDF5 type");
    //    }

    //    int count = values.Count;
    //    byte[] buffer = new byte[elementSize * count];
    //    for (int i = 0; i < count; i++)
    //    {
    //        byte[] bytes = convert(values[i]);
    //        Buffer.BlockCopy(bytes, 0, buffer, i * elementSize, elementSize);
    //    }

    //    // Check if dataset exists
    //    if (H5L.exists(_fileId, datasetName) > 0)
    //    {
    //        datasetId = H5D.open(_fileId, datasetName);
    //        dataspaceId = H5D.get_space(datasetId);
    //        H5S.get_simple_extent_dims(dataspaceId, dims, null);
    //        H5S.close(dataspaceId);

    //        currentSize = dims[0];
    //        newSize = currentSize + (ulong)count;

    //        H5D.set_extent(datasetId, new ulong[] { newSize });
    //    }
    //    else
    //    {
    //        ulong[] maxDims = { H5S.UNLIMITED };
    //        ulong[] chunkDims = { 1024 };

    //        dataspaceId = H5S.create_simple(1, new ulong[] { 0 }, maxDims);
    //        long propId = H5P.create(H5P.DATASET_CREATE);
    //        H5P.set_chunk(propId, 1, chunkDims);

    //        datasetId = H5D.create(_fileId, datasetName, hdf5Type, dataspaceId, H5P.DEFAULT, propId);

    //        H5P.close(propId);
    //        H5S.close(dataspaceId);

    //        currentSize = 0;
    //        newSize = (ulong)count;

    //        H5D.set_extent(datasetId, new ulong[] { newSize });
    //    }

    //    // Select the write location (hyperslab)
    //    dataspaceId = H5D.get_space(datasetId);
    //    H5S.select_hyperslab(dataspaceId, H5S.seloper_t.SET,
    //        new ulong[] { currentSize }, null, new ulong[] { (ulong)count }, null);

    //    long memspaceId = H5S.create_simple(1, new ulong[] { (ulong)count }, null);

    //    GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
    //    try
    //    {
    //        H5D.write(datasetId, hdf5Type, memspaceId, dataspaceId, H5P.DEFAULT, handle.AddrOfPinnedObject());
    //    }
    //    finally
    //    {
    //        handle.Free();
    //    }

    //    H5S.close(dataspaceId);
    //    H5S.close(memspaceId);
    //    H5D.close(datasetId);
    //}



    //public void AddValue(string datasetName, object value, long hdf5Type)
    //{
    //    long datasetId;
    //    long dataspaceId;
    //    ulong[] dims = new ulong[1];
    //    ulong newSize;
    //    byte[] buffer;

    //    // Convert value to bytes based on HDF5 type
    //    if (hdf5Type == H5T.IEEE_F32LE)
    //        buffer = BitConverter.GetBytes(Convert.ToSingle(value));
    //    else if (hdf5Type == H5T.NATIVE_LONG)
    //        buffer = BitConverter.GetBytes(Convert.ToInt64(value));
    //    else
    //        throw new ArgumentException("Unsupported HDF5 type");

    //    // Check if dataset exists
    //    if (H5L.exists(_fileId, datasetName) > 0)
    //    {
    //        datasetId = H5D.open(_fileId, datasetName);
    //        dataspaceId = H5D.get_space(datasetId);
    //        H5S.get_simple_extent_dims(dataspaceId, dims, null);
    //        H5S.close(dataspaceId);

    //        newSize = dims[0] + 1;
    //        H5D.set_extent(datasetId, new ulong[] { newSize });
    //    }
    //    else
    //    {
    //        // Create a new extendable dataset
    //        ulong[] maxDims = { H5S.UNLIMITED };
    //        ulong[] chunkDims = { 1024 };

    //        dataspaceId = H5S.create_simple(1, new ulong[] { 0 }, maxDims);
    //        long propId = H5P.create(H5P.DATASET_CREATE);
    //        H5P.set_chunk(propId, 1, chunkDims);

    //        datasetId = H5D.create(_fileId, datasetName, hdf5Type, dataspaceId, H5P.DEFAULT, propId);

    //        H5P.close(propId);
    //        H5S.close(dataspaceId);

    //        newSize = 1;
    //        H5D.set_extent(datasetId, new ulong[] { newSize });
    //    }

    //    // Select the last position to append
    //    dataspaceId = H5D.get_space(datasetId);
    //    H5S.select_hyperslab(dataspaceId, H5S.seloper_t.SET, new ulong[] { newSize - 1 }, null, new ulong[] { 1 }, null);

    //    long memspaceId = H5S.create_simple(1, new ulong[] { 1 }, null);

    //    GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

    //    try
    //    {
    //        H5D.write(datasetId, hdf5Type, memspaceId, dataspaceId, H5P.DEFAULT, handle.AddrOfPinnedObject());
    //    }
    //    finally
    //    {
    //        handle.Free();
    //    }

    //    H5S.close(dataspaceId);
    //    H5S.close(memspaceId);
    //    H5D.close(datasetId);
    //}

    /// <summary>
    /// Append a full blittable struct as one record, growing the dataset
    /// only every GrowStep rows for high throughput.  No unsafe code needed.
    /// </summary>
    //public void AddValue<T>(string dsName, T value) where T : struct
    //{
    //    const int GrowStep = 1024;                      // extend in blocks

    //    int structSize = Marshal.SizeOf<T>();

    //    // --------------------------------------------------------------------
    //    // 1. open (or create) dataset and discover current row count
    //    // --------------------------------------------------------------------
    //    long dsetId, fileSpace;
    //    ulong curRows;

    //    if (H5L.exists(_fileId, dsName) > 0)
    //    {
    //        dsetId = H5D.open(_fileId, dsName);
    //        fileSpace = H5D.get_space(dsetId);
    //        ulong[] dims = new ulong[1];
    //        H5S.get_simple_extent_dims(fileSpace, dims, null);
    //        H5S.close(fileSpace);
    //        curRows = dims[0];
    //    }
    //    else
    //    {
    //        // first time: create chunked, unlimited dataset and pre-extend
    //        ulong[] maxDims = { H5S.UNLIMITED };
    //        ulong[] chunkDims = { GrowStep };

    //        long space0 = H5S.create_simple(1, new ulong[] { 0 }, maxDims);

    //        long dcpl = H5P.create(H5P.DATASET_CREATE);
    //        H5P.set_chunk(dcpl, 1, chunkDims);

    //        long typeId = H5T.create(H5T.class_t.OPAQUE, new IntPtr(structSize));

    //        dsetId = H5D.create(_fileId, dsName, typeId, space0, H5P.DEFAULT, dcpl);

    //        H5T.close(typeId);
    //        H5P.close(dcpl);
    //        H5S.close(space0);

    //        curRows = 0;
    //        H5D.set_extent(dsetId, new ulong[] { (ulong)GrowStep });
    //    }

    //    // grow block-wise, not every row
    //    if (curRows % (ulong)GrowStep == 0 && curRows != 0)
    //        H5D.set_extent(dsetId, new ulong[] { curRows + (ulong)GrowStep });

    //    // --------------------------------------------------------------------
    //    // 2. pin the struct (boxed) – no unsafe code
    //    // --------------------------------------------------------------------
    //    GCHandle h = GCHandle.Alloc(value, GCHandleType.Pinned);
    //    try
    //    {
    //        fileSpace = H5D.get_space(dsetId);
    //        H5S.select_hyperslab(fileSpace, H5S.seloper_t.SET,
    //                             new ulong[] { curRows }, null,
    //                             new ulong[] { 1 }, null);

    //        long memSpace = H5S.create_simple(1, new ulong[] { 1 }, null);

    //        H5D.write(dsetId, H5T.NATIVE_OPAQUE, memSpace, fileSpace,
    //                  H5P.DEFAULT, h.AddrOfPinnedObject());

    //        H5S.close(fileSpace);
    //        H5S.close(memSpace);
    //    }
    //    finally
    //    {
    //        h.Free();
    //        H5D.close(dsetId);
    //    }
    //}


    //public void AddValue(string datasetName, float value)
    //{
    //    long datasetId;
    //    long dataspaceId;
    //    ulong[] dims = new ulong[1];

    //    // Check if dataset exists
    //    if (H5L.exists(_fileId, datasetName) > 0)
    //    {
    //        datasetId = H5D.open(_fileId, datasetName);

    //        dataspaceId = H5D.get_space(datasetId);
    //        H5S.get_simple_extent_dims(dataspaceId, dims, null);
    //        H5S.close(dataspaceId);

    //        ulong newSize = dims[0] + 1;
    //        H5D.set_extent(datasetId, new ulong[] { newSize });
    //    }
    //    else
    //    {
    //        // First time creation
    //        ulong[] maxDims = { H5S.UNLIMITED };
    //        ulong[] chunkDims = { 1024 }; // Chunking required for unlimited

    //        dataspaceId = H5S.create_simple(1, new ulong[] { 0 }, maxDims);

    //        long propId = H5P.create(H5P.DATASET_CREATE);
    //        H5P.set_chunk(propId, 1, chunkDims);

    //        datasetId = H5D.create(_fileId, datasetName, H5T.IEEE_F32LE, dataspaceId, H5P.DEFAULT, propId);

    //        H5P.close(propId);
    //        H5S.close(dataspaceId);

    //        // Extend to size 1
    //        H5D.set_extent(datasetId, new ulong[] { 1 });
    //    }

    //    // Now write the new value at the end
    //    dataspaceId = H5D.get_space(datasetId);
    //    H5S.select_hyperslab(dataspaceId, H5S.seloper_t.SET, new ulong[] { dims[0] }, null, new ulong[] { 1 }, null);

    //    // Create memory space for one float
    //    long memspaceId = H5S.create_simple(1, new ulong[] { 1 }, null);

    //    byte[] buffer = BitConverter.GetBytes(value);
    //    GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

    //    try
    //    {
    //        H5D.write(datasetId, H5T.IEEE_F32LE, memspaceId, dataspaceId, H5P.DEFAULT, handle.AddrOfPinnedObject());
    //    }
    //    finally
    //    {
    //        handle.Free();
    //    }

    //    H5S.close(dataspaceId);
    //    H5S.close(memspaceId);
    //    H5D.close(datasetId);
    //}

    public void Dispose()
    {
        if (_fileId >= 0)
        {
            H5F.close(_fileId);
            _fileId = -1;
        }
    }
}
