﻿// Copyright (c) 2023 - 2024, Owners of https://github.com/ag2ai
// SPDX-License-Identifier: Apache-2.0
// Contributions to this project, i.e., https://github.com/ag2ai/ag2, 
// are licensed under the Apache License, Version 2.0 (Apache-2.0).
// Portions derived from  https://github.com/microsoft/autogen under the MIT License.
// SPDX-License-Identifier: MIT
// Copyright (c) Microsoft Corporation. All rights reserved.
// Example05_Dalle_And_GPT4V.cs

using AutoGen.Core;
using AutoGen.OpenAI.V1;
using AutoGen.OpenAI.V1.Extension;
using Azure.AI.OpenAI;
using FluentAssertions;
using autogen = AutoGen.LLMConfigAPI;

public partial class Example05_Dalle_And_GPT4V
{
    private readonly OpenAIClient openAIClient;

    public Example05_Dalle_And_GPT4V(OpenAIClient openAIClient)
    {
        this.openAIClient = openAIClient;
    }

    /// <summary>
    /// Generate image from prompt using DALL-E.
    /// </summary>
    /// <param name="prompt">prompt with feedback</param>
    /// <returns></returns>
    [Function]
    public async Task<string> GenerateImage(string prompt)
    {
        // TODO
        // generate image from prompt using DALL-E
        // and return url.
        var option = new ImageGenerationOptions
        {
            Size = ImageSize.Size1024x1024,
            Style = ImageGenerationStyle.Vivid,
            ImageCount = 1,
            Prompt = prompt,
            Quality = ImageGenerationQuality.Standard,
            DeploymentName = "dall-e-3",
        };

        var imageResponse = await openAIClient.GetImageGenerationsAsync(option);
        var imageUrl = imageResponse.Value.Data.First().Url.OriginalString;

        return $@"// ignore this line [IMAGE_GENERATION]
The image is generated from prompt {prompt}

{imageUrl}";
    }

    public static async Task RunAsync()
    {
        // This example shows how to use DALL-E and GPT-4V to generate image from prompt and feedback.
        // The DALL-E agent will generate image from prompt.
        // The GPT-4V agent will provide feedback to DALL-E agent to help it generate better image.
        // The conversation will be terminated when the image satisfies the condition.
        // The image will be saved to image.jpg in current directory.

        // get OpenAI Key and create config
        var openAIKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new Exception("Please set OPENAI_API_KEY environment variable.");
        var gpt35Config = autogen.GetOpenAIConfigList(openAIKey, new[] { "gpt-3.5-turbo" });
        var gpt4vConfig = autogen.GetOpenAIConfigList(openAIKey, new[] { "gpt-4-vision-preview" });
        var openAIClient = new OpenAIClient(openAIKey);
        var instance = new Example05_Dalle_And_GPT4V(openAIClient);
        var imagePath = Path.Combine("resource", "images", "background.png");
        if (File.Exists(imagePath))
        {
            File.Delete(imagePath);
        }

        var generateImageFunctionMiddleware = new FunctionCallMiddleware(
            functions: [instance.GenerateImageFunctionContract],
            functionMap: new Dictionary<string, Func<string, Task<string>>>
            {
                { nameof(GenerateImage), instance.GenerateImageWrapper },
            });
        var dalleAgent = new OpenAIChatAgent(
            openAIClient: openAIClient,
            modelName: "gpt-3.5-turbo",
            name: "dalle",
            systemMessage: "You are a DALL-E agent that generate image from prompt, when conversation is terminated, return the most recent image url")
            .RegisterMessageConnector()
            .RegisterStreamingMiddleware(generateImageFunctionMiddleware)
            .RegisterMiddleware(async (msgs, option, agent, ct) =>
            {
                if (msgs.Any(msg => msg.GetContent()?.ToLower().Contains("approve") is true))
                {
                    return new TextMessage(Role.Assistant, $"The image satisfies the condition, conversation is terminated. {GroupChatExtension.TERMINATE}");
                }

                var msgsWithoutImage = msgs.Where(msg => msg is not ImageMessage).ToList();
                var reply = await agent.GenerateReplyAsync(msgsWithoutImage, option, ct);

                if (reply.GetContent() is string content && content.Contains("IMAGE_GENERATION"))
                {
                    var imageUrl = content.Split("\n").Last();
                    var imageMessage = new ImageMessage(Role.Assistant, imageUrl, from: reply.From, mimeType: "image/png");

                    Console.WriteLine($"download image from {imageUrl} to {imagePath}");
                    var httpClient = new HttpClient();
                    var imageBytes = await httpClient.GetByteArrayAsync(imageUrl, ct);
                    File.WriteAllBytes(imagePath, imageBytes);

                    return imageMessage;
                }
                else
                {
                    return reply;
                }
            })
            .RegisterPrintMessage();

        var gpt4VAgent = new OpenAIChatAgent(
            openAIClient: openAIClient,
            name: "gpt4v",
            modelName: "gpt-4-vision-preview",
            systemMessage: @"You are a critism that provide feedback to DALL-E agent.
Carefully check the image generated by DALL-E agent and provide feedback.
If the image satisfies the condition, then say [APPROVE].
Otherwise, provide detailed feedback to DALL-E agent so it can generate better image.

The image should satisfy the following conditions:
- There should be a cat and a mouse in the image
- The cat should be chasing after the mouse")
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        await gpt4VAgent.InitiateChatAsync(
            receiver: dalleAgent,
            message: "Hey dalle, please generate image from prompt: English short hair blue cat chase after a mouse",
            maxRound: 10);

        File.Exists(imagePath).Should().BeTrue();
    }
}
