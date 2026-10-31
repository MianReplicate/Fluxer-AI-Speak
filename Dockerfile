FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Clone main project
RUN git clone https://github.com/MianReplicate/Fluxer-AI-Speak.git Fluxer-AI-Speak

# Clone dependency
RUN git clone https://github.com/MianReplicate/OpenRouter.NET.git OpenRouter.NET

WORKDIR /src/Fluxer-AI-Speak

# Restore dependencies
RUN dotnet restore

# Publish
RUN dotnet publish Fluxer-AI-Speak.csproj \
        -c Release \
        -r linux-x64 \
        --self-contained true \
        -p:PublishSingleFile=true \
        -o /app/publish/

FROM ubuntu:24.04 AS runtime

WORKDIR /app/publish/

COPY --from=build /app/publish/ .

RUN chmod +x ./Fluxer-AI-Speak

ENTRYPOINT ["./Fluxer-AI-Speak"]