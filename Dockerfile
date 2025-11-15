# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY MomentApp/MomentApp.csproj MomentApp/
RUN dotnet restore "MomentApp/MomentApp.csproj"

# Copy everything else and build
COPY MomentApp/ MomentApp/
WORKDIR /src/MomentApp
RUN dotnet build "MomentApp.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "MomentApp.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .

# Set environment variable for port
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "MomentApp.dll"]
