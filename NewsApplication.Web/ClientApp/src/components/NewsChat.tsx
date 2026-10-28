import { useState, type FormEvent, type KeyboardEvent } from 'react';
import { LoaderCircle, RotateCcw, Send, Sparkles } from 'lucide-react';

import {
    resetNewsChat,
    sendNewsChatMessage,
    type ChatResponse,
} from '../api';

interface Props {
    scopeKey?: string | null;
    scopeLabel?: string | null;
    visibleArticleIds?: string[];
    onResult: (response: ChatResponse, question: string) => void;
}

export default function NewsChat({
    scopeKey,
    scopeLabel,
    visibleArticleIds = [],
    onResult,
}: Props) {
    const [conversationId, setConversationId] = useState<string | null>(null);
    const [draft, setDraft] = useState('');
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [conversationScope, setConversationScope] = useState<ChatResponse['scope']>(undefined);

    const submit = async (event?: FormEvent) => {
        event?.preventDefault();
        const message = draft.trim();
        if (!message || loading) return;

        setLoading(true);
        setError(null);

        try {
            const activeScopeKey = conversationScope?.scopeKey ?? scopeKey ?? undefined;
            const activeScopeLabel = conversationScope?.label ?? scopeLabel ?? undefined;
            const response = await sendNewsChatMessage({
                conversationId,
                message,
                context: {
                    scopeKey: activeScopeKey,
                    scopeLabel: activeScopeLabel,
                    visibleArticleIds,
                },
            });

            setConversationId(response.conversationId);
            if (response.scope) setConversationScope(response.scope);
            setDraft('');
            onResult(response, message);
        } catch (caught) {
            setError(caught instanceof Error ? caught.message : 'The news assistant is unavailable.');
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
        setDraft('');
        setError(null);
    };

    const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
        if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            void submit();
        }
    };

    const activeScopeLabel = conversationScope?.label ?? scopeLabel;

    return (
        <section className="news-ai-composer" aria-label="Ask the news assistant">
            <form className="news-ai-composer__form" onSubmit={submit}>
                <span className="news-ai-composer__icon" aria-hidden>
                    <Sparkles size={22} />
                </span>
                <div className="news-ai-composer__field">
                    <textarea
                        value={draft}
                        onChange={event => setDraft(event.target.value)}
                        onKeyDown={onKeyDown}
                        placeholder={activeScopeLabel
                            ? `Ask AI about ${activeScopeLabel}…`
                            : 'Ask AI about the news in any city or country…'}
                        rows={1}
                        disabled={loading}
                        aria-label="Ask the news assistant"
                    />
                    <span className={`news-ai-composer__status ${error ? 'is-error' : ''}`} role="status">
                        {error
                            ? 'Could not get an answer. Please try again.'
                            : loading
                                ? 'Finding the most relevant reporting…'
                                : 'Press Enter to ask · Shift + Enter for a new line'}
                    </span>
                </div>
                {conversationId && (
                    <button
                        type="button"
                        className="news-ai-composer__reset"
                        onClick={() => void reset()}
                        aria-label="Start a new AI conversation"
                        title="New conversation"
                    >
                        <RotateCcw size={18} />
                    </button>
                )}
                <button
                    type="submit"
                    className={`news-ai-composer__send ${loading ? 'is-loading' : ''}`}
                    disabled={loading || !draft.trim()}
                    aria-label="Ask AI"
                >
                    {loading ? <LoaderCircle size={21} /> : <Send size={20} />}
                </button>
            </form>
        </section>
    );
}
