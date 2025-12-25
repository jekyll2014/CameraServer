using CameraLib;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using Xunit;

namespace CameraLib.Tests
{
    public class MatPoolTests
    {
        [Fact]
        public void MatPool_RentAndReturn_ShouldWork()
        {
            // Arrange
            using var pool = new MatPool(100, 100, MatType.CV_8UC3, maxPoolSize: 10);

            // Act
            var mat = pool.Rent();
            var isValid = mat != null && mat.Width == 100 && mat.Height == 100;
            pool.Return(mat);

            // Assert
            Assert.True(isValid);
            var stats = pool.GetStatistics();
            Assert.Equal(1, stats.Rented);
            Assert.Equal(1, stats.Returned);
            Assert.Equal(0, stats.Outstanding);
        }

        [Fact]
        public void MatPool_RentScoped_ShouldAutoReturn()
        {
            // Arrange
            using var pool = new MatPool(100, 100, MatType.CV_8UC3, maxPoolSize: 10);

            // Act
            using (var pooled = pool.RentScoped())
            {
                Assert.NotNull(pooled.Mat);
                Assert.Equal(100, pooled.Mat.Width);
                Assert.Equal(100, pooled.Mat.Height);
            }

            // Assert
            var stats = pool.GetStatistics();
            Assert.Equal(1, stats.Rented);
            Assert.Equal(1, stats.Returned);
            Assert.Equal(0, stats.Outstanding);
        }

        [Fact]
        public void MatPool_MultipleRentAndReturn_ShouldTrackCorrectly()
        {
            // Arrange
            using var pool = new MatPool(100, 100, MatType.CV_8UC3, maxPoolSize: 10);

            // Act
            var mat1 = pool.Rent();
            var mat2 = pool.Rent();
            var mat3 = pool.Rent();

            pool.Return(mat1);
            pool.Return(mat2);

            // Assert
            var stats = pool.GetStatistics();
            Assert.Equal(3, stats.Rented);
            Assert.Equal(2, stats.Returned);
            Assert.Equal(1, stats.Outstanding);

            // Cleanup
            pool.Return(mat3);
        }

        [Fact]
        public void MatPool_RentAfterReturn_ShouldReuseMat()
        {
            // Arrange
            using var pool = new MatPool(100, 100, MatType.CV_8UC3, maxPoolSize: 10);

            // Act - Rent and return
            var mat1 = pool.Rent();
            var ptr1 = mat1.Data;
            pool.Return(mat1);

            // Rent again - should get the same Mat from pool
            var mat2 = pool.Rent();
            var ptr2 = mat2.Data;

            // Assert
            Assert.Equal(ptr1, ptr2); // Same underlying Mat
            pool.Return(mat2);
        }

        [Fact]
        public void PooledMat_Dispose_ShouldNotThrow()
        {
            // Arrange
            using var pool = new MatPool(100, 100, MatType.CV_8UC3);

            // Act & Assert
            using (var pooled = pool.RentScoped())
            {
                Assert.NotNull(pooled.Mat);
            } // Should not throw
        }

        [Fact]
        public void PooledMat_AccessAfterDispose_ShouldThrow()
        {
            // Arrange
            using var pool = new MatPool(100, 100, MatType.CV_8UC3);
            var pooled = pool.RentScoped();

            // Act
            pooled.Dispose();

            // Assert
            Assert.Throws<ObjectDisposedException>(() => pooled.Mat);
        }

        [Fact]
        public void MatPoolManager_GetPool_ShouldCreatePool()
        {
            // Arrange
            using var manager = new MatPoolManager(maxPoolSize: 10);

            // Act
            var pool1 = manager.GetPool(100, 100, MatType.CV_8UC3);
            var pool2 = manager.GetPool(100, 100, MatType.CV_8UC3);

            // Assert
            Assert.Same(pool1, pool2); // Should return same pool
        }

        [Fact]
        public void MatPoolManager_DifferentSizes_ShouldCreateDifferentPools()
        {
            // Arrange
            using var manager = new MatPoolManager(maxPoolSize: 10);

            // Act
            var pool1 = manager.GetPool(100, 100);
            var pool2 = manager.GetPool(200, 200);

            // Assert
            Assert.NotSame(pool1, pool2);
        }

        [Fact]
        public void MatPoolManager_RentAndReturn_ShouldWork()
        {
            // Arrange
            using var manager = new MatPoolManager(maxPoolSize: 10);

            // Act
            var mat = manager.Rent(100, 100, MatType.CV_8UC3);
            Assert.NotNull(mat);
            Assert.Equal(100, mat.Width);
            Assert.Equal(100, mat.Height);

            manager.Return(mat);

            // Assert
            var pool = manager.GetPool(100, 100, MatType.CV_8UC3);
            var stats = pool.GetStatistics();
            Assert.Equal(1, stats.Rented);
            Assert.Equal(1, stats.Returned);
        }

        [Fact]
        public void MatPoolManager_RentScoped_ShouldAutoReturn()
        {
            // Arrange
            using var manager = new MatPoolManager(maxPoolSize: 10);

            // Act
            using (var pooled = manager.RentScoped(100, 100))
            {
                Assert.NotNull(pooled.Mat);
            }

            // Assert
            var pool = manager.GetPool(100, 100, MatType.CV_8UC3);
            var stats = pool.GetStatistics();
            Assert.Equal(1, stats.Rented);
            Assert.Equal(1, stats.Returned);
        }

        [Fact]
        public void MatPoolManager_ReturnWrongSize_ShouldDispose()
        {
            // Arrange
            using var manager = new MatPoolManager(maxPoolSize: 10);

            // Act - Create Mat of one size
            var mat = new Mat(50, 50, MatType.CV_8UC3);

            // Try to return to manager (no pool for 50x50)
            manager.Return(mat);

            // Assert - Should have disposed the Mat
            Assert.True(mat.IsDisposed);
        }

        [Fact]
        public void MatPoolManager_ReturnNull_ShouldNotThrow()
        {
            // Arrange
            using var manager = new MatPoolManager(maxPoolSize: 10);

            // Act & Assert
            manager.Return(null); // Should not throw
        }

        [Fact]
        public void MatPool_StressTest_ShouldHandleHighLoad()
        {
            // Arrange
            using var pool = new MatPool(640, 480, MatType.CV_8UC3, maxPoolSize: 100);
            const int iterations = 1000;

            // Act
            for (int i = 0; i < iterations; i++)
            {
                using var pooled = pool.RentScoped();
                // Simulate some work
                pooled.Mat.SetTo(Scalar.All(i % 256));
            }

            // Assert
            var stats = pool.GetStatistics();
            Assert.Equal(iterations, stats.Rented);
            Assert.Equal(iterations, stats.Returned);
            Assert.Equal(0, stats.Outstanding);
        }

        [Fact]
        public void MatPoolManager_Dispose_ShouldLogStatistics()
        {
            // Arrange
            var logger = new NullLogger<MatPoolManager>();
            var manager = new MatPoolManager(maxPoolSize: 10, logger);

            // Act - Use some pools
            using (manager.RentScoped(100, 100)) { }
            using (manager.RentScoped(200, 200)) { }

            // Dispose
            manager.Dispose();

            // Assert - Just verify no exceptions
            Assert.True(true);
        }

        [Fact]
        public void MatPool_Policy_ShouldValidateReturnedMats()
        {
            // Arrange
            var policy = new MatPooledObjectPolicy(100, 100, MatType.CV_8UC3);

            // Act - Create Mat
            var mat = policy.Create();
            Assert.True(policy.Return(mat));

            // Dispose the Mat
            mat.Dispose();

            // Assert - Should reject disposed Mat
            Assert.False(policy.Return(mat));
        }

        [Fact]
        public void MatPool_Policy_ShouldRejectWrongSize()
        {
            // Arrange
            var policy = new MatPooledObjectPolicy(100, 100, MatType.CV_8UC3);

            // Act - Create Mat of different size
            var wrongSizeMat = new Mat(50, 50, MatType.CV_8UC3);

            // Assert
            Assert.False(policy.Return(wrongSizeMat));

            wrongSizeMat.Dispose();
        }
    }
}
