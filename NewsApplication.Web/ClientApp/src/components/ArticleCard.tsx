import { motion } from 'framer-motion';
import { Calendar, ExternalLink } from 'lucide-react';

import type { ArticleDto } from '../types';

    interface Props {
        article: ArticleDto;
        index: number;
    }

    const gradientPalette: string[] = [
        'linear-gradient(135deg, rgba(72, 107, 255, 0.25), rgba(63, 251, 255, 0.12))',
        'linear-gradient(145deg, rgba(255, 171, 102, 0.24), rgba(255, 94, 196, 0.12))',
        'linear-gradient(145deg, rgba(105, 221, 255, 0.28), rgba(51, 142, 255, 0.12))',
        'linear-gradient(145deg, rgba(121, 90, 255, 0.26), rgba(41, 203, 255, 0.12))',
        'linear-gradient(145deg, rgba(255, 214, 102, 0.22), rgba(255, 140, 54, 0.1))',
        'linear-gradient(145deg, rgba(255, 255, 255, 0.18), rgba(91, 187, 255, 0.14))',
    ];

    export default function ArticleCard({ article, index }: Props) {
        const date = article.publishedUtc ? new Date(article.publishedUtc) : null;
        const hasImage = !!article.imageUrl?.trim();
        const gradient = gradientPalette[index % gradientPalette.length];
        const description = article.description ?? article.snippet;

    return (
        <motion.a
            href={article.url}
            target="_blank"
            rel="noreferrer"
            className="modern-article-card"
            initial={{ opacity: 0, y: 16, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: -10, scale: 0.98 }}
            transition={{ duration: 0.35, delay: index * 0.05 }}
            whileHover={{ y: -6, scale: 1.02 }}
        >
            <div className="modern-card-media">
                {hasImage ? (
                    <motion.img
                        src={article.imageUrl}
                        alt=""
                        className="modern-card-img"
                        initial={{ scale: 1.05 }}
                        whileHover={{ scale: 1.12 }}
                        transition={{ duration: 0.6 }}
                        loading="lazy"
                    />
                ) : (
                    <div className="modern-card-placeholder" style={{ background: gradient }}>
                        <div className="modern-card-placeholder-glow" />
                    </div>
                )}

                <div className="modern-card-tint" />

                <div className="modern-card-badge">
                    <span>{article.sourceName ?? 'Source'}</span>
                </div>

                <motion.div
                    className="modern-card-link"
                    whileHover={{ scale: 1.08 }}
                    aria-hidden
                >
                    <ExternalLink size={14} />
                </motion.div>
            </div>

            <div className="modern-card-body">
                <h4 className="modern-card-title" title={article.title}>
                    {article.title}
                </h4>

                {description && (
                    <p className="modern-card-description">
                        {description}
                    </p>
                )}

                <div className="modern-card-meta">
                    {date && (
                        <>
                            <Calendar size={14} />
                            <span>
                                {date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })}
                            </span>
                        </>
                    )}
                </div>
            </div>
            <motion.div
                className="modern-card-accent"
                initial={{ scaleX: 0 }}
                whileHover={{ scaleX: 1 }}
                transition={{ duration: 0.3 }}
            />
        </motion.a>
    );
}
