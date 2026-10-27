from __future__ import annotations

from pydantic import BaseModel, ConfigDict, Field


class ApiModel(BaseModel):
    model_config = ConfigDict(populate_by_name=True)


class ChatContext(ApiModel):
    scope_key: str | None = Field(default=None, alias="scopeKey", max_length=500)
    scope_label: str | None = Field(default=None, alias="scopeLabel", max_length=200)
    visible_article_ids: list[str] = Field(default_factory=list, alias="visibleArticleIds", max_length=24)


class ChatRequest(ApiModel):
    conversation_id: str | None = Field(default=None, alias="conversationId", max_length=100)
    message: str = Field(min_length=1, max_length=2000)
    context: ChatContext | None = None


class Citation(ApiModel):
    article_id: str = Field(alias="articleId")
    title: str
    publisher: str
    url: str
    published_time: str | None = Field(default=None, alias="publishedTime")


class ResolvedScope(ApiModel):
    scope_key: str = Field(alias="scopeKey")
    label: str | None = None


class ChatResponse(ApiModel):
    conversation_id: str = Field(alias="conversationId")
    answer: str
    scope: ResolvedScope | None = None
    citations: list[Citation] = Field(default_factory=list)
    suggested_questions: list[str] = Field(default_factory=list, alias="suggestedQuestions")


class HealthResponse(ApiModel):
    status: str
    backend: str
    ollama: str
    model: str
