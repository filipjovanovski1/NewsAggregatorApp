import { motion } from 'framer-motion';
import { Calendar, ExternalLink, X } from 'lucide-react';
import { createPortal } from 'react-dom';
import type { ArticleDto } from '../types';

interface Props {
    article: ArticleDto;
    onClose: () => void;
}

export default function ArticleDetailPopup({ article, onClose }: Props) {
    const title = article.translatedTitle?.trim() || article.title;
    const description = article.summary?.trim() || article.description?.trim() || article.snippet?.trim();
    const date = article.publishedUtc ? new Date(article.publishedUtc) : null;
    const titleId = `article-detail-title-${article.id.replace(/[^a-zA-Z0-9_-]/g, '-')}`;

    return createPortal(
        <motion.div
            className="article-detail-backdrop"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
            onClick={onClose}
            onTouchStart={(event) => event.stopPropagation()}
            onTouchMove={(event) => event.stopPropagation()}
            onTouchEnd={(event) => event.stopPropagation()}
        >
            <motion.section
                className="article-detail-surface"
                role="dialog"
                aria-modal="true"
                aria-labelledby={titleId}
                initial={{ opacity: 0, scale: 0.94, y: 20 }}
                animate={{ opacity: 1, scale: 1, y: 0 }}
                exit={{ opacity: 0, scale: 0.96, y: 10 }}
                transition={{ duration: 0.25, ease: [0.25, 0.46, 0.45, 0.94] }}
                onClick={(event) => event.stopPropagation()}
            >
                <header className="article-detail-header">
                    <h2 id={titleId}>{title}</h2>
                    <button
                        type="button"
                        className="article-overlay-close article-detail-close"
                        onClick={onClose}
                        aria-label="Close article details"
                        autoFocus
                    >
                        <X size={16} />
                    </button>
                </header>

                <div className="article-detail-content">
                    <div className="article-detail-media">
                        {article.imageUrl?.trim() ? (
                            <img src={article.imageUrl} alt="" />
                        ) : (
                            <div className="article-detail-placeholder" aria-hidden />
                        )}
                        <span className="article-detail-source">{article.sourceName ?? 'Source'}</span>
                    </div>

                    <div className="article-detail-copy">
                        {description && <p>{description}</p>}
                        <footer className="article-detail-meta">
                            {date && (
                                <span>
                                    <Calendar size={14} />
                                    {date.toLocaleDateString(undefined, {
                                        month: 'short',
                                        day: 'numeric',
                                        year: 'numeric'
                                    })}
                                </span>
                            )}
                            <a href={article.url} target="_blank" rel="noreferrer">
                                Read original
                                <ExternalLink size={14} />
                            </a>
                        </footer>
                    </div>
                </div>
            </motion.section>
        </motion.div>,
        document.body
    );
}
