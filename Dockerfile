FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/MangaStore.API/MangaStore.API.csproj", "src/MangaStore.API/"]
COPY ["src/MangaStore.Application/MangaStore.Application.csproj", "src/MangaStore.Application/"]
COPY ["src/MangaStore.Domain/MangaStore.Domain.csproj", "src/MangaStore.Domain/"]
COPY ["src/MangaStore.Infrastructure/MangaStore.Infrastructure.csproj", "src/MangaStore.Infrastructure/"]
RUN dotnet restore "src/MangaStore.API/MangaStore.API.csproj"
COPY . .
WORKDIR "/src/src/MangaStore.API"
RUN dotnet build "MangaStore.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "MangaStore.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MangaStore.API.dll"]
