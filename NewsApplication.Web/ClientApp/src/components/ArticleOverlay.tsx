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
    const [activeIndex, setActiveIndex] = useState(0);
    const [touchStart, setTouchStart] = useState<number | null>(null);
    const [touchEnd, setTouchEnd] = useState<number | null>(null);

    // Minimum swipe distance (in px)
    const minSwipeDistance = 50;

    const onTouchStart = (e: React.TouchEvent) => {
        setTouchEnd(null);
        setTouchStart(e.targetTouches[0].clientX);
    };

    const onTouchMove = (e: React.TouchEvent) => {
        setTouchEnd(e.targetTouches[0].clientX);
    };

    const onTouchEnd = () => {
        if (!touchStart || !touchEnd) return;

        const distance = touchStart - touchEnd;
        const isLeftSwipe = distance > minSwipeDistance;
        const isRightSwipe = distance < -minSwipeDistance;

        if (isLeftSwipe && canNext) {
            setDirection(1);
            onNext();
        }
        if (isRightSwipe && canPrev) {
            setDirection(-1);
            onPrev();
        }
    };

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

    // Adjust items per page based on screen size
    const getItemsToShow = () => {
        if (typeof window === 'undefined') return 6;
        const width = window.innerWidth;
        if (width < 640) return 2; // Mobile: 1 column, 2 items
        if (width < 1024) return 4; // Tablet: 2 columns, 4 items
        return 6; // Desktop: 3 columns, 6 items
    };

    const [itemsToShow, setItemsToShow] = useState(getItemsToShow());

    useEffect(() => {
        const handleResize = () => {
            setItemsToShow(getItemsToShow());
        };

        window.addEventListener('resize', handleResize);
        return () => window.removeEventListener('resize', handleResize);
    }, []);

    const shown = useMemo(() => items.slice(0, itemsToShow), [items, itemsToShow]);

    const previousArticle = activeIndex > 0
        ? items[activeIndex - 1]
        : null;

    const activeArticle = items[activeIndex] ?? null;

    const nextArticle = activeIndex < items.length - 1
        ? items[activeIndex + 1]
        : null;

    const totalNum = total ?? items.length;
    const totalPages = Math.max(1, Math.ceil(totalNum / pageSize));

    const pageDots = Array.from({ length: Math.min(totalPages, 5) }, (_, i) => i + 1);

    const handlePrev = () => {
        setDirection(-1);

        if (activeIndex > 0) {
            setActiveIndex((current) => current - 1);
        } else if (canPrev) {
            onPrev();
        }
    };

    const handleNext = () => {
        setDirection(1);

        if (activeIndex < items.length - 1) {
            setActiveIndex((current) => current + 1);
        } else if (canNext) {
            onNext();
        }
    };

    useEffect(() => {
        setActiveIndex(0);
    }, [page]);

    // Get icon size based on screen - scaled for 15.6"
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
                    onClick={(e) => e.stopPropagation()}
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
                            <div className="article-overlay-pages">
                                {pageDots.map((dot) => (
                                    <motion.span
                                        key={dot}
                                        className={`page-dot ${dot === page ? 'active' : ''}`}
                                        animate={{
                                            width: dot === page ? 28 : 9,
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
                                <X size={getIconSize()} />
                            </motion.button>
                        </div>
                    </div>
                    <div className="article-overlay-body">
                        <motion.button
                            onClick={handlePrev}
                            disabled={activeIndex === 0 && !canPrev}
                            className="article-overlay-arrow"
                            whileHover={canPrev ? { scale: 1.05 } : {}}
                            whileTap={canPrev ? { scale: 0.96 } : {}}
                            aria-label="Previous page"
                        >
                            <ChevronLeft size={18} />
                        </motion.button>


                        <div className="article-overlay-grid">
                            <div className="article-carousel-stage">
                                <AnimatePresence initial={false}>
                                    {items.map((article, index) => {
                                        const offset = index - activeIndex;

                                        // Only render cards close to the active one
                                        if (Math.abs(offset) > 2) return null;

                                        return (
                                            <motion.div
                                                key={article.url}
                                                className={`article-carousel-moving-card ${
                                                    offset === 0 ? 'is-active' : ''
                                                }`}
                                                initial={false}
                                                animate={{
                                                    x:
                                                        offset === 0
                                                            ? '0%'
                                                            : offset === -1
                                                            ? '-112%'
                                                            : offset === 1
                                                            ? '112%'
                                                            : offset < 0
                                                            ? '-225%'
                                                            : '225%',
                                                    scale: offset === 0 ? 1.12 : 0.92,
                                                    opacity:
                                                        Math.abs(offset) <= 1
                                                            ? offset === 0
                                                                ? 1
                                                                : 0.55
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
                                                    opacity: {
                                                        duration: 0.25,
                                                    },
                                                }}
                                            >
                                                <ArticleCard
                                                    article={article}
                                                    index={index}
                                                    active={offset === 0}
                                                />
                                            </motion.div>
                                        );
                                    })}
                                </AnimatePresence>
                            </div>
                        </div>

                        <motion.button
                            onClick={handleNext}
                            disabled={activeIndex === items.length - 1 && !canNext}
                            className="article-overlay-arrow"
                            whileHover={canNext ? { scale: 1.05 } : {}}
                            whileTap={canNext ? { scale: 0.96 } : {}}
                            aria-label="Next page"
                        >
                            <ChevronRight size={18} />
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
                                    animate={{ width: dot === page ? 16 : 7 }}
                                />
                            ))}
                        </div>
                    </div>
                </motion.div>
            </motion.div>
        </AnimatePresence>
    );
}