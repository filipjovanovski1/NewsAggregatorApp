import { useEffect, useMemo, useState } from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { ChevronLeft, ChevronRight, MapPin, Newspaper, X } from 'lucide-react';

import type { ArticleDto } from '../types';
import ArticleCard from './ArticleCard';

interface Props {
    items: ArticleDto[];
    total?: number;
    page: number;
    pageSize: number;
    onPrev: () => void;
    onNext: () => void;
    canPrev: boolean;
    canNext: boolean;
    onClose: () => void;
    title?: string;
}

export default function ArticleOverlay({
    items,
    total,
    page,
    pageSize,
    onPrev,
    onNext,
    canPrev,
    canNext,
    onClose,
    title,
}: Props) {

    const [direction, setDirection] = useState(0);

    useEffect(() => {
        const onKey = (e: KeyboardEvent) => {
            if (e.key === 'ArrowLeft' && canPrev) {
                setDirection(-1);
                onPrev();
            }
            if (e.key === 'ArrowRight' && canNext) {
                setDirection(1);
                onNext();
            }
            if (e.key === 'Escape') onClose();
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [onPrev, onNext, onClose, canPrev, canNext]);

    const shown = useMemo(() => items.slice(0, 6), [items]);
    const totalNum = total ?? items.length;
    const totalPages = Math.max(1, Math.ceil(totalNum / pageSize));

    const pageDots = Array.from({ length: Math.min(totalPages, 5) }, (_, i) => i + 1);

    const handlePrev = () => {
        setDirection(-1);
        onPrev();
    };

    const handleNext = () => {
        setDirection(1);
        onNext();
    };

    return (
        <AnimatePresence>
            <motion.div
                className="article-overlay-backdrop"
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: 0.25 }}
            >
                <motion.div
                    className="article-overlay-surface"
                    initial={{ opacity: 0, scale: 0.95, y: 30 }}
                    animate={{ opacity: 1, scale: 1, y: 0 }}
                    exit={{ opacity: 0, scale: 0.95, y: 10 }}
                    transition={{ duration: 0.35, ease: [0.25, 0.46, 0.45, 0.94] }}
                >
                    <div className="article-overlay-glow glow-a" aria-hidden />
                    <div className="article-overlay-glow glow-b" aria-hidden />

                    <div className="article-overlay-header">
                        <div className="article-overlay-title">
                            <div className="article-overlay-icon">
                                <MapPin size={18} />
                            </div>
                            <div>
                                <h3>{title ?? 'Articles'}</h3>
                                <p>
                                    <Newspaper size={14} />
                                    <span>{totalNum} articles found</span>
                                </p>
                            </div>
                        </div>

                        <div className="article-overlay-actions">
                            <div className="article-overlay-pages">
                                {pageDots.map((dot) => (
                                    <motion.span
                                        key={dot}
                                        className={`page-dot ${dot === page ? 'active' : ''}`}
                                        animate={{
                                            width: dot === page ? 32 : 10,
                                            backgroundColor: dot === page ? 'rgba(89, 166, 255, 1)' : 'rgba(255,255,255,0.16)',
                                        }}
                                        transition={{ duration: 0.25 }}
                                    />
                                ))}
                                {totalPages > 5 && (
                                    <span className="article-overlay-more">+{totalPages - 5}</span>
                                )}
                            </div>

                            <motion.button
                                onClick={onClose}
                                className="article-overlay-close"
                                whileHover={{ scale: 1.04 }}
                                whileTap={{ scale: 0.95 }}
                                aria-label="Close overlay"
                            >
                                <X size={18} />
                            </motion.button>
                        </div>
                    </div>
                    <div className="article-overlay-body">
                        <motion.button
                            onClick={handlePrev}
                            disabled={!canPrev}
                            className="article-overlay-arrow"
                            whileHover={canPrev ? { scale: 1.05 } : {}}
                            whileTap={canPrev ? { scale: 0.96 } : {}}
                            aria-label="Previous page"
                        >
                            <ChevronLeft size={20} />
                        </motion.button>

                        <div className="article-overlay-grid">
                            <AnimatePresence mode="wait" custom={direction}>
                                <motion.div
                                    key={page}
                                    custom={direction}
                                    initial={{ x: direction > 0 ? 40 : -40, opacity: 0, scale: 0.98 }}
                                    animate={{ x: 0, opacity: 1, scale: 1 }}
                                    exit={{ x: direction > 0 ? -40 : 40, opacity: 0, scale: 0.98 }}
                                    transition={{
                                        x: { type: 'spring', stiffness: 320, damping: 32 },
                                        opacity: { duration: 0.2 },
                                    }}
                                >
                                    <div className="article-overlay-grid-inner">
                                        {shown.map((article, idx) => (
                                            <ArticleCard key={article.id} article={article} index={idx} />
                                        ))}
                                    </div>
                                </motion.div>
                            </AnimatePresence>
                        </div>

                        <motion.button
                            onClick={handleNext}
                            disabled={!canNext}
                            className="article-overlay-arrow"
                            whileHover={canNext ? { scale: 1.05 } : {}}
                            whileTap={canNext ? { scale: 0.96 } : {}}
                            aria-label="Next page"
                        >
                            <ChevronRight size={20} />
                        </motion.button>
                    </div>
                    <div className="article-overlay-footer">
                        <span>
                            Page {page} of {totalPages}
                        </span>
                        <div className="article-overlay-dots">
                            {pageDots.map((dot) => (
                                <motion.span
                                    key={dot}
                                    className={`page-dot ${dot === page ? 'active' : ''}`}
                                    animate={{ width: dot === page ? 18 : 8 }}
                                />
                            ))}
                        </div>
                    </div>
                </motion.div>
            </motion.div>
        </AnimatePresence>
    );
}
