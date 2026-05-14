# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["ECourtTracker.API.csproj", "./"]
RUN dotnet restore "ECourtTracker.API.csproj"
COPY . .
RUN dotnet build "ECourtTracker.API.csproj" -c Release -o /app/build
RUN dotnet publish "ECourtTracker.API.csproj" -c Release -o /app/publish

# Run stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ECourtTracker.API.dll"]
