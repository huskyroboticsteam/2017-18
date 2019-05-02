# Only works for OpenCV version 4+
# Expects all dependencies to be preinstalled

OPENCV_VERSION=4.0.1
ARCH_BIN=6.2
GCC_VERSION=6 # Version 6 required for CUDA 9
GXX_VERSION=6
WORKING_DIR=$(pwd)

wget http://github.com/opencv/opencv/archive/${OPENCV_VERSION}.zip
unzip ${OPENCV_VERSION}.zip
rm ${OPENCV_VERSION}.zip

wget http://github.com/opencv/opencv_contrib/archive/${OPENCV_VERSION}.zip
unzip ${OPENCV_VERSION}.zip
rm ${OPENCV_VERSION}.zip

cd opencv-${OPENCV_VERSION}
mkdir build
cd build

cmake -DCMAKE_C_COMPILER=gcc-${GCC_VERSION} -DCMAKE_CXX_COMPILER=g++-${GXX_VERSION} -DCMAKE_BUILD_TYPE=RELEASE -DCMAKE_INSTALL_PREFIX=/usr/local -DWITH_CUDA=ON -DCUDA_ARCH_BIN=${ARCH_BIN} -DCUDA_ARCH_PTX="" -DWITH_CUBLAS=ON -DWITH_GSTREAMER=ON -DWITH_QT=ON -DWITH_OPENGL=ON -DBUILD_opencv_java=OFF -DOPENCV_GENERATE_PKGCONFIG=ON -DOPENCV_EXTRA_MODULES_PATH=${WORKING_DIR}/opencv_contrib-${OPENCV_VERSION}/modules -DENABLE_FAST_MATH=ON -DCUDA_FAST_MATH=ON -DBUILD_EXAMPLES=OFF -DBUILD_DOCS=OFF -DBUILD_PERF_TESTS=OFF -DBUILD_TESTS=OFF -DWITH_NVCUVID=ON ..

make -j6
sudo make install
sudo ldconfig
