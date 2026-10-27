import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import { Bot, ExternalLink, LoaderCircle, MessageCircle, RotateCcw, Send, X } from 'lucide-react';

import {
    resetNewsChat,
    sendNewsChatMessage,
    type ChatCitation,
    type ChatResponse,
} from '../api';

interface Props {
    scopeKey?: string | null;
    scopeLabel?: string | null;
}

type UiMessage = {
    id: number;
    role: 'assistant' | 'user';
    text: string;
    citations?: ChatCitation[];
};

const welcomeMessage: UiMessage = {
    id: 0,
    role: 'assistant',
    text: 'Ask me about the RSS news stored in this app. I will answer with article citations.',
};

export default function NewsChat({ scopeKey, scopeLabel }: Props) {
    const [open, setOpen] = useState(false);
    const [conversationId, setConversationId] = useState<string | null>(null);
    const [messages, setMessages] = useState<UiMessage[]>([welcomeMessage]);
    const [draft, setDraft] = useState('');
    const [loading, setLoading] = useState(false);
    const [conversationScope, setConversationScope] = useState<ChatResponse['scope']>(undefined);
    const messageId = useRef(1);
    const scrollArea = useRef<HTMLDivElement>(null);

    useEffect(() => {
        if (!open) return;
        scrollArea.current?.scrollTo({ top: scrollArea.current.scrollHeight, behavior: 'smooth' });
    }, [messages, loading, open]);

    const submit = async (event?: FormEvent) => {
        event?.preventDefault();
        const message = draft.trim();
        if (!message || loading) return;

        setDraft('');
        setMessages(current => [
            ...current,
            { id: messageId.current++, role: 'user', text: message },
        ]);
        setLoading(true);

        try {
            const activeScopeKey = conversationScope?.scopeKey ?? scopeKey ?? undefined;
            const activeScopeLabel = conversationScope?.label ?? scopeLabel ?? undefined;
            const response = await sendNewsChatMessage({
                conversationId,
                message,
                context: {
                    scopeKey: activeScopeKey,
                    scopeLabel: activeScopeLabel,
                    visibleArticleIds: [],
                },
            });
            setConversationId(response.conversationId);
            if (response.scope) setConversationScope(response.scope);
            setMessages(current => [
                ...current,
                {
                    id: messageId.current++,
                    role: 'assistant',
                    text: response.answer,
                    citations: response.citations,
                },
            ]);
        } catch (error) {
            const detail = error instanceof Error ? error.message : 'Unknown error';
            setMessages(current => [
                ...current,
                {
                    id: messageId.current++,
                    role: 'assistant',
                    text: `I could not complete that request. ${detail}`,
                },
            ]);
        } finally {
            setLoading(false);
        }
    };

    const reset = async () => {
        if (conversationId) {
            await resetNewsChat(conversationId).catch(() => undefined);
        }
        setConversationId(null);
        setConversationScope(undefined);
        setMessages([welcomeMessage]);
        setDraft('');
    };

    const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
        if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            void submit();
        }
    };

    if (!open) {
        return (
            <button className="news-chat-launcher" onClick={() => setOpen(true)} aria-label="Open news chatbot">
                <MessageCircle size={22} />
                <span>Ask AI</span>
            </button>
        );
    }

    const activeScopeLabel = conversationScope?.label ?? scopeLabel;

    return (
        <section className="news-chat-panel" aria-label="News chatbot">
            <header className="news-chat-header">
                <div className="news-chat-heading">
                    <span className="news-chat-avatar"><Bot size={18} /></span>
                    <div>
                        <strong>News assistant</strong>
                        <span>{activeScopeLabel ? `Using ${activeScopeLabel}` : 'Searches stored RSS articles'}</span>
                    </div>
                </div>
                <div className="news-chat-header-actions">
                    <button onClick={() => void reset()} aria-label="Reset conversation" title="Reset conversation">
                        <RotateCcw size={15} />
                    </button>
                    <button onClick={() => setOpen(false)} aria-label="Close chatbot">
                        <X size={17} />
                    </button>
                </div>
            </header>

            <div className="news-chat-messages" ref={scrollArea} aria-live="polite">
                {messages.map(message => (
                    <article key={message.id} className={`news-chat-message news-chat-message--${message.role}`}>
                        <p>{message.text}</p>
                        {!!message.citations?.length && (
                            <div className="news-chat-citations">
                                {message.citations.map((citation, index) => (
                                    <a key={citation.articleId} href={citation.url} target="_blank" rel="noreferrer">
                                        <span>{index + 1}</span>
                                        <div>
                                            <strong>{citation.title}</strong>
                                            <small>{citation.publisher}</small>
                                        </div>
                                        <ExternalLink size={13} />
                                    </a>
                                ))}
                            </div>
                        )}
                    </article>
                ))}
                {loading && (
                    <div className="news-chat-thinking">
                        <LoaderCircle size={16} />
                        <span>Searching articles and preparing an answer…</span>
                    </div>
                )}
            </div>

            <form className="news-chat-form" onSubmit={submit}>
                <textarea
                    value={draft}
                    onChange={event => setDraft(event.target.value)}
                    onKeyDown={onKeyDown}
                    placeholder={activeScopeLabel ? `Ask about ${activeScopeLabel}…` : 'Ask about news in a location…'}
                    rows={2}
                    disabled={loading}
                    aria-label="Message the news assistant"
                />
                <button type="submit" disabled={loading || !draft.trim()} aria-label="Send message">
                    <Send size={17} />
                </button>
            </form>
            <footer className="news-chat-disclaimer">Answers use articles in your local database.</footer>
        </section>
    );
}
