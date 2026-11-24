# ############################
# Build and publish the ASP.NET Core API.
# ############################
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Paperless.sln
RUN dotnet publish src/Paperless.Api/Paperless.Api.csproj -c Release -o /app/publish --no-restore

# ############################
# Run the published API in the smaller ASP.NET runtime image.
# ############################
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y curl fonts-dejavu-core fontconfig && rm -rf /var/lib/apt/lists/*
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Paperless.Api.dll"]
