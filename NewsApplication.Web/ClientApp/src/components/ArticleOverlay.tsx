import { useCallback, useEffect, useMemo, useState, type TouchEvent } from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { ChevronLeft, ChevronRight, LoaderCircle, MapPin, Newspaper, X } from 'lucide-react';

import type { ArticleDto } from '../types';
import ArticleCard from './ArticleCard';
import ArticleDetailPopup from './ArticleDetailPopup';

interface Props {
    items: ArticleDto[];
    total?: number;
    hasMore: boolean;
    onLoadMore: () => Promise<void>;
    onClose: () => void;
    title?: string;
}

export default function ArticleOverlay({
    items,
    total,
    hasMore,
    onLoadMore,
    onClose,
    title,
}: Props) {
    const [activeIndex, setActiveIndex] = useState(0);
    const [selectedArticle, setSelectedArticle] = useState<ArticleDto | null>(null);
    const [touchStart, setTouchStart] = useState<number | null>(null);
    const [touchEnd, setTouchEnd] = useState<number | null>(null);

    const readyItems = useMemo(
        () => items.filter(article =>
            article.summaryStatus === 'ready' &&
            Boolean(article.translatedTitle?.trim()) &&
            Boolean(article.summary?.trim())),
        [items]
    );
    const pendingCount = items.filter(article => article.summaryStatus === 'pending').length;
    const requiredInitialItems = Math.min(2, items.length);
    const isPreparingInitialItems = items.length === 0 ||
        (readyItems.length < requiredInitialItems && pendingCount > 0);
    const canMovePrev = activeIndex > 0;
    const canMoveNext = activeIndex < readyItems.length - 1;
    const firstItemId = items[0]?.id ?? '';

    useEffect(() => {
        setActiveIndex(0);
        setSelectedArticle(null);
    }, [firstItemId]);

    useEffect(() => {
        if (readyItems.length === 0) {
            setActiveIndex(0);
            return;
        }

        setActiveIndex(current => Math.min(current, readyItems.length - 1));
    }, [readyItems.length]);

    useEffect(() => {
        if (!hasMore || pendingCount > 0 || readyItems.length === 0) return;
        if (activeIndex < Math.max(0, readyItems.length - 2)) return;
        void onLoadMore();
    }, [activeIndex, hasMore, onLoadMore, pendingCount, readyItems.length]);

    const handlePrev = useCallback(() => {
        if (!canMovePrev) return;
        setActiveIndex(current => Math.max(0, current - 1));
    }, [canMovePrev]);

    const handleNext = useCallback(() => {
        if (!canMoveNext) return;
        setActiveIndex(current => Math.min(readyItems.length - 1, current + 1));
    }, [canMoveNext, readyItems.length]);

    useEffect(() => {
        const onKey = (event: KeyboardEvent) => {
            if (selectedArticle) {
                if (event.key === 'Escape') setSelectedArticle(null);
                return;
            }
            if (event.key === 'ArrowLeft') handlePrev();
            if (event.key === 'ArrowRight') handleNext();
            if (event.key === 'Escape') onClose();
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [handleNext, handlePrev, onClose, selectedArticle]);

    const onTouchStart = (event: TouchEvent) => {
        setTouchEnd(null);
        setTouchStart(event.targetTouches[0].clientX);
    };

    const onTouchMove = (event: TouchEvent) => {
        setTouchEnd(event.targetTouches[0].clientX);
    };

    const onTouchEnd = () => {
        if (touchStart == null || touchEnd == null) return;
        const distance = touchStart - touchEnd;
        if (distance > 50) handleNext();
        if (distance < -50) handlePrev();
    };

    const totalNum = total ?? items.length;
    const carouselFrames = Math.max(1, readyItems.length);
    const currentFrame = readyItems.length === 0 ? 0 : activeIndex;
    const firstVisibleDot = Math.min(
        Math.max(0, currentFrame - 2),
        Math.max(0, carouselFrames - 5)
    );
    const visibleDots = Array.from(
        { length: Math.min(carouselFrames, 5) },
        (_, index) => firstVisibleDot + index
    );
    const currentStory = readyItems.length === 0 ? 0 : activeIndex + 1;

    const getIconSize = () => {
        if (typeof window === 'undefined') return 16;
        return window.innerWidth < 640 ? 14 : 16;
    };

    return (
        <AnimatePresence>
            <motion.div
                className="article-overlay-backdrop"
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: 0.25 }}
                onClick={onClose}
            >
                <motion.div
                    className="article-overlay-surface"
                    initial={{ opacity: 0, scale: 0.95, y: 30 }}
                    animate={{ opacity: 1, scale: 1, y: 0 }}
                    exit={{ opacity: 0, scale: 0.95, y: 10 }}
                    transition={{ duration: 0.35, ease: [0.25, 0.46, 0.45, 0.94] }}
                    onClick={(event) => event.stopPropagation()}
                    onTouchStart={onTouchStart}
                    onTouchMove={onTouchMove}
                    onTouchEnd={onTouchEnd}
                >
                    <div className="article-overlay-glow glow-a" aria-hidden />
                    <div className="article-overlay-glow glow-b" aria-hidden />

                    <div className="article-overlay-header">
                        <div className="article-overlay-title">
                            <div className="article-overlay-icon">
                                <MapPin size={getIconSize()} />
                            </div>
                            <div>
                                <h3>{title ?? 'Articles'}</h3>
                                <p>
                                    <Newspaper size={13} />
                                    <span>{totalNum} articles found</span>
                                </p>
                            </div>
                        </div>

                        <div className="article-overlay-actions">
                            <div className="article-overlay-pages" aria-label="Carousel position">
                                {visibleDots.map(dot => (
                                    <motion.span
                                        key={dot}
                                        className={`page-dot ${dot === currentFrame ? 'active' : ''}`}
                                        animate={{
                                            width: dot === currentFrame ? 28 : 9,
                                            backgroundColor: dot === currentFrame
                                                ? 'rgba(89, 166, 255, 1)'
                                                : 'rgba(255,255,255,0.16)',
                                        }}
                                        transition={{ duration: 0.25 }}
                                    />
                                ))}
                                {carouselFrames > 5 && (
                                    <span className="article-overlay-more">+{carouselFrames - 5}</span>
                                )}
                            </div>

                            <motion.button
                                onClick={onClose}
                                className="article-overlay-close"
                                whileHover={{ scale: 1.04 }}
                                whileTap={{ scale: 0.95 }}
                                aria-label="Close overlay"
                            >
                                <X size={getIconSize()} />
                            </motion.button>
                        </div>
                    </div>

                    <div className="article-overlay-body">
                        <motion.button
                            onClick={handlePrev}
                            disabled={!canMovePrev || isPreparingInitialItems}
                            className="article-overlay-arrow"
                            whileHover={canMovePrev ? { scale: 1.05 } : {}}
                            whileTap={canMovePrev ? { scale: 0.96 } : {}}
                            aria-label="Previous news"
                        >
                            <ChevronLeft size={18} />
                        </motion.button>

                        <div className="article-overlay-grid">
                            {isPreparingInitialItems ? (
                                <div className="article-translation-state" role="status">
                                    <LoaderCircle className="article-translation-spinner" size={28} />
                                    <strong>Translating your news</strong>
                                    <span>
                                        {readyItems.length} of {Math.max(2, requiredInitialItems)} first stories ready
                                    </span>
                                </div>
                            ) : readyItems.length === 0 ? (
                                <div className="article-translation-state article-translation-state--error" role="status">
                                    <strong>No translated stories are available</strong>
                                    <span>Check that Ollama is running, then search again.</span>
                                </div>
                            ) : (
                                <div className="article-carousel-stage">
                                    <AnimatePresence initial={false}>
                                        {readyItems.map((article, index) => {
                                            const offset = index - activeIndex;
                                            if (Math.abs(offset) > 2) return null;

                                            return (
                                                <motion.div
                                                    key={article.id}
                                                    className={`article-carousel-moving-card${offset === 0 ? ' is-active' : ''}`}
                                                    initial={false}
                                                    animate={{
                                                        x: offset === 0
                                                            ? '0%'
                                                            : offset === -1
                                                                ? 'calc(-112% - 1vw)'
                                                                : offset === 1
                                                                    ? 'calc(112% + 1vw)'
                                                                    : offset < 0
                                                                        ? 'calc(-225% - 2vw)'
                                                                        : 'calc(225% + 2vw)',
                                                        scale: offset === 0 ? 1 : 0.92,
                                                        opacity: Math.abs(offset) <= 1
                                                            ? offset === 0 ? 1 : 0.55
                                                            : 0,
                                                        zIndex: offset === 0 ? 3 : 1,
                                                    }}
                                                    transition={{
                                                        x: {
                                                            type: 'spring',
                                                            stiffness: 180,
                                                            damping: 24,
                                                            mass: 0.9,
                                                        },
                                                        scale: {
                                                            type: 'spring',
                                                            stiffness: 180,
                                                            damping: 24,
                                                        },
                                                        opacity: { duration: 0.25 },
                                                    }}
                                                >
                                                    <ArticleCard
                                                        article={article}
                                                        index={index}
                                                        onOpen={setSelectedArticle}
                                                    />
                                                </motion.div>
                                            );
                                        })}
                                    </AnimatePresence>
                                </div>
                            )}
                        </div>

                        <motion.button
                            onClick={handleNext}
                            disabled={!canMoveNext || isPreparingInitialItems}
                            className="article-overlay-arrow"
                            whileHover={canMoveNext ? { scale: 1.05 } : {}}
                            whileTap={canMoveNext ? { scale: 0.96 } : {}}
                            aria-label="More news"
                        >
                            <ChevronRight size={18} />
                        </motion.button>
                    </div>

                    <div className="article-overlay-footer">
                        <span>
                            Story {currentStory} of {readyItems.length} translated
                        </span>
                        <div className="article-overlay-dots">
                            {visibleDots.map(dot => (
                                <motion.span
                                    key={dot}
                                    className={`page-dot ${dot === currentFrame ? 'active' : ''}`}
                                    animate={{ width: dot === currentFrame ? 16 : 7 }}
                                />
                            ))}
                        </div>
                    </div>

                    <AnimatePresence>
                        {selectedArticle && (
                            <ArticleDetailPopup
                                article={selectedArticle}
                                onClose={() => setSelectedArticle(null)}
                            />
                        )}
                    </AnimatePresence>
                </motion.div>
            </motion.div>
        </AnimatePresence>
    );
}
