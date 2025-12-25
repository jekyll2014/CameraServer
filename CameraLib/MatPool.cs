using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;

using OpenCvSharp;

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CameraLib
{
    /// <summary>
    /// Policy for creating and managing Mat objects in the pool.
    /// </summary>
    public class MatPooledObjectPolicy : IPooledObjectPolicy<Mat>
    {
        private readonly int _width;
        private readonly int _height;
        private readonly MatType _matType;

        public MatPooledObjectPolicy(int width, int height, MatType? matType = null)
        {
            _width = width;
            _height = height;
            _matType = matType ?? MatType.CV_8UC3;
        }

        public Mat Create()
        {
            return _width > 0 && _height > 0
                ? new Mat(_height, _width, _matType)
                : new Mat();
        }

        public bool Return(Mat obj)
        {
            if (obj == null || obj.IsDisposed)
                return false;

            // Validate the Mat is still the expected size and type
            if (_width > 0 && _height > 0)
            {
                if (obj.Width != _width || obj.Height != _height || obj.Type() != _matType)
                    return false;
            }

            // Clear the Mat data for reuse (optional, depends on use case)
            // obj.SetTo(Scalar.All(0));

            return true;
        }
    }

    /// <summary>
    /// High-performance object pool for OpenCV Mat objects.
    /// Reduces allocation pressure and GC overhead.
    /// </summary>
    public sealed class MatPool : IDisposable
    {
        private readonly ObjectPool<Mat> _pool;
        private readonly int _width;
        private readonly int _height;
        private readonly MatType _matType;
        private readonly ILogger? _logger;
        private long _rentCount;
        private long _returnCount;
        private bool _disposedValue;

        public MatPool(int width, int height, MatType? matType = null, int maxPoolSize = 100, ILogger? logger = null)
        {
            _width = width;
            _height = height;
            _matType = matType ?? MatType.CV_8UC3;
            _logger = logger;

            var policy = new MatPooledObjectPolicy(width, height, _matType);
            var provider = new DefaultObjectPoolProvider
            {
                MaximumRetained = maxPoolSize
            };
            _pool = provider.Create(policy);

            _logger?.LogInformation(
                "MatPool created: {Width}x{Height}, Type: {MatType}, MaxSize: {MaxPoolSize}",
                width, height, matType, maxPoolSize);
        }

        /// <summary>
        /// Rent a Mat from the pool. Caller MUST return it using Return() or use RentScoped().
        /// </summary>
        public Mat Rent()
        {
            var mat = _pool.Get();
            Interlocked.Increment(ref _rentCount);
            return mat;
        }

        /// <summary>
        /// Return a Mat to the pool. Do not use the Mat after returning it.
        /// </summary>
        public void Return(Mat mat)
        {
            if (mat == null)
                return;

            _pool.Return(mat);
            Interlocked.Increment(ref _returnCount);
        }

        /// <summary>
        /// Rent a Mat with automatic return via IDisposable pattern.
        /// Use with 'using' statement for guaranteed cleanup.
        /// </summary>
        public PooledMat RentScoped()
        {
            return new PooledMat(this);
        }

        /// <summary>
        /// Get pool statistics.
        /// </summary>
        public (long Rented, long Returned, long Outstanding) GetStatistics()
        {
            var rented = Interlocked.Read(ref _rentCount);
            var returned = Interlocked.Read(ref _returnCount);
            return (rented, returned, rented - returned);
        }

        public void Dispose()
        {
            if (!_disposedValue)
            {
                var stats = GetStatistics();
                _logger?.LogInformation(
                    "MatPool disposing: Rented={Rented}, Returned={Returned}, Outstanding={Outstanding}",
                    stats.Rented, stats.Returned, stats.Outstanding);

                if (stats.Outstanding > 0)
                {
                    _logger?.LogWarning(
                        "MatPool has {Outstanding} outstanding Mat objects that were not returned",
                        stats.Outstanding);
                }

                _disposedValue = true;
            }
        }
    }

    /// <summary>
    /// RAII wrapper for pooled Mat objects. Automatically returns Mat to pool on disposal.
    /// </summary>
    public sealed class PooledMat : IDisposable
    {
        private readonly MatPool _pool;
        private Mat? _mat;
        private bool _disposed;

        internal PooledMat(MatPool pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _mat = pool.Rent();
        }

        /// <summary>
        /// Get the underlying Mat. Do not use after disposing this PooledMat.
        /// </summary>
        public Mat Mat
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(PooledMat));
                return _mat ?? throw new InvalidOperationException("Mat is null");
            }
        }

        public void Dispose()
        {
            if (!_disposed && _mat != null)
            {
                _pool.Return(_mat);
                _mat = null;
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Manages multiple Mat pools for different sizes.
    /// Automatically selects the best pool based on requested size.
    /// </summary>
    public sealed class MatPoolManager : IDisposable
    {
        private readonly ConcurrentDictionary<(int Width, int Height, MatType Type), MatPool> _pools = new();
        private readonly int _maxPoolSize;
        private readonly ILogger? _logger;
        private bool _disposedValue;

        public MatPoolManager(int maxPoolSize = 100, ILogger? logger = null)
        {
            _maxPoolSize = maxPoolSize;
            _logger = logger;
        }

        /// <summary>
        /// Get or create a pool for the specified size and type.
        /// </summary>
        public MatPool GetPool(int width, int height, MatType? matType = null)
        {
            var type = matType ?? MatType.CV_8UC3;
            var key = (width, height, type);
            return _pools.GetOrAdd(key, k => new MatPool(k.Width, k.Height, k.Type, _maxPoolSize, _logger));
        }

        /// <summary>
        /// Rent a Mat from the appropriate pool.
        /// </summary>
        public Mat Rent(int width, int height, MatType? matType = null)
        {
            var pool = GetPool(width, height, matType);
            return pool.Rent();
        }

        /// <summary>
        /// Return a Mat to the appropriate pool.
        /// </summary>
        public void Return(Mat mat)
        {
            if (mat == null || mat.IsDisposed)
                return;

            var key = (mat.Width, mat.Height, mat.Type());
            if (_pools.TryGetValue(key, out var pool))
            {
                pool.Return(mat);
            }
            else
            {
                // No pool for this size, dispose it
                _logger?.LogDebug(
                    "No pool found for Mat {Width}x{Height} Type={Type}, disposing",
                    mat.Width, mat.Height, mat.Type());
                mat.Dispose();
            }
        }

        /// <summary>
        /// Rent a Mat with automatic return via IDisposable pattern.
        /// </summary>
        public PooledMat RentScoped(int width, int height, MatType? matType = null)
        {
            var pool = GetPool(width, height, matType);
            return pool.RentScoped();
        }

        /// <summary>
        /// Get statistics for all pools.
        /// </summary>
        public void LogStatistics()
        {
            if (_logger == null)
                return;

            _logger.LogInformation("MatPoolManager Statistics:");
            foreach (var kvp in _pools)
            {
                var stats = kvp.Value.GetStatistics();
                _logger.LogInformation(
                    "  Pool {Width}x{Height} {Type}: Rented={Rented}, Returned={Returned}, Outstanding={Outstanding}",
                    kvp.Key.Width, kvp.Key.Height, kvp.Key.Type, stats.Rented, stats.Returned, stats.Outstanding);
            }
        }

        public void Dispose()
        {
            if (!_disposedValue)
            {
                LogStatistics();

                foreach (var pool in _pools.Values)
                {
                    pool.Dispose();
                }

                _pools.Clear();
                _disposedValue = true;
            }
        }
    }
}
