#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

#FROM emgu/emgucv-ubi:ubi9-dotnet8 AS base
FROM emgu/bazel-android:dotnet-6.0-bazel-4.2.1 AS base
#FROM emgu/bazel-android AS base
#FROM emgu/vs2019_buildtools_cuda_openvino:openvino_2021.3.394 AS base
#USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["CameraServer/CameraServer.csproj", "CameraServer/"]
RUN dotnet restore "./CameraServer/./CameraServer.csproj"
COPY . .
WORKDIR "/src/CameraServer"
RUN dotnet build "./CameraServer.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./CameraServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
#RUN yum install libavcodec-dev libavformat-dev libavutil-dev libswscale-dev libavresample-dev
RUN apt-get install -y libavcodec-dev libavformat-dev libavutil-dev libswscale-dev
RUN apt-get install -y ffmpeg
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CameraServer.dll"]