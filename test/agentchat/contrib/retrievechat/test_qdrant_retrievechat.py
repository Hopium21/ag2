# Copyright (c) 2023 - 2024, Owners of https://github.com/ag2ai
#
# SPDX-License-Identifier: Apache-2.0
#
# Portions derived from  https://github.com/microsoft/autogen are under the MIT License.
# SPDX-License-Identifier: MIT
#!/usr/bin/env python3 -m pytest

import os
import sys
from collections.abc import Generator

import pytest

from autogen import AssistantAgent, config_list_from_json

from ....conftest import Credentials, reason, skip_openai  # noqa: E402

try:
    import fastembed
    from qdrant_client import QdrantClient

    from autogen.agentchat.contrib.qdrant_retrieve_user_proxy_agent import (
        QdrantRetrieveUserProxyAgent,
        create_qdrant_from_dir,
        query_qdrant,
    )

    QDRANT_INSTALLED = True
except ImportError:
    QDRANT_INSTALLED = False

try:
    import openai
except ImportError:
    skip = True
else:
    skip = False or skip_openai


@pytest.mark.skipif(
    sys.platform in ["darwin", "win32"] or not QDRANT_INSTALLED or skip,
    reason="do not run on MacOS or windows OR dependency is not installed OR requested to skip",
)
def test_retrievechat(credentials_gpt_4o_mini: Credentials):
    conversations = {}
    # ChatCompletion.start_logging(conversations)  # deprecated in v0.2

    assistant = AssistantAgent(
        name="assistant",
        system_message="You are a helpful assistant.",
        llm_config={
            "timeout": 600,
            "seed": 42,
            "config_list": credentials_gpt_4o_mini.config_list,
        },
    )

    client = QdrantClient(":memory:")
    ragproxyagent = QdrantRetrieveUserProxyAgent(
        name="ragproxyagent",
        human_input_mode="NEVER",
        max_consecutive_auto_reply=2,
        retrieve_config={
            "client": client,
            "docs_path": "./website/docs",
            "chunk_token_size": 2000,
        },
    )

    assistant.reset()

    code_problem = "How can I use FLAML to perform a classification task, set use_spark=True, train 30 seconds and force cancel jobs if time limit is reached."
    ragproxyagent.initiate_chat(assistant, message=ragproxyagent.message_generator, problem=code_problem, silent=True)
    print(conversations)


@pytest.mark.skipif(not QDRANT_INSTALLED, reason="qdrant_client is not installed")
def test_qdrant_filter():
    client = QdrantClient(":memory:")
    create_qdrant_from_dir(dir_path="./website/docs", client=client, collection_name="autogen-docs")
    results = query_qdrant(
        query_texts=["How can I use AutoGen UserProxyAgent and AssistantAgent to do code generation?"],
        n_results=4,
        client=client,
        collection_name="autogen-docs",
        # Return only documents with "AutoGen" in the string
        search_string="AutoGen",
    )
    assert len(results["ids"][0]) == 4


@pytest.mark.skipif(not QDRANT_INSTALLED, reason="qdrant_client is not installed")
def test_qdrant_search():
    test_dir = os.path.join(os.path.dirname(__file__), "../../..", "test_files")
    client = QdrantClient(":memory:")
    create_qdrant_from_dir(test_dir, client=client)

    assert client.get_collection("all-my-documents")

    # Perform a semantic search without any filter
    results = query_qdrant(["autogen"], client=client)
    assert isinstance(results, dict) and any("autogen" in res[0].lower() for res in results.get("documents", []))


if __name__ == "__main__":
    test_retrievechat()
    test_qdrant_filter()
    test_qdrant_search()
