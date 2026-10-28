import { useEffect, useMemo, useState } from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { ChevronLeft, ChevronRight, MapPin, Newspaper, Sparkles, X } from 'lucide-react';

import type { ArticleDto } from '../types';
import ArticleCard from './ArticleCard';

export type AssistantCarouselResult = {
    question: string;
    answer: string;
};

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
    assistantResult?: AssistantCarouselResult | null;
}

type CarouselSlide =
    | { kind: 'answer'; value: AssistantCarouselResult }
    | { kind: 'article'; value: ArticleDto; articleIndex: number };

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
    assistantResult,
}: Props) {
    const [activeIndex, setActiveIndex] = useState(0);
    const [touchStart, setTouchStart] = useState<number | null>(null);
    const [touchEnd, setTouchEnd] = useState<number | null>(null);

    const slides = useMemo<CarouselSlide[]>(() => [
        ...(assistantResult ? [{ kind: 'answer' as const, value: assistantResult }] : []),
        ...items.map((article, articleIndex) => ({
            kind: 'article' as const,
            value: article,
            articleIndex,
        })),
    ], [assistantResult, items]);

    const isAssistantView = !!assistantResult;
    const totalNum = total ?? items.length;
    const totalPages = Math.max(1, Math.ceil(totalNum / pageSize));
    const pageDots = Array.from({ length: Math.min(totalPages, 5) }, (_, index) => index + 1);
    const slideDots = Array.from({ length: Math.min(slides.length, 8) }, (_, index) => index);
    const hasPrevious = activeIndex > 0 || (!isAssistantView && canPrev);
    const hasNext = activeIndex < slides.length - 1 || (!isAssistantView && canNext);

    const handlePrev = () => {
        if (activeIndex > 0) {
            setActiveIndex(current => current - 1);
        } else if (!isAssistantView && canPrev) {
            onPrev();
        }
    };

    const handleNext = () => {
        if (activeIndex < slides.length - 1) {
            setActiveIndex(current => current + 1);
        } else if (!isAssistantView && canNext) {
            onNext();
        }
    };

    useEffect(() => {
        setActiveIndex(0);
    }, [page, assistantResult, items]);

    useEffect(() => {
        const onKey = (event: KeyboardEvent) => {
            if (event.key === 'ArrowLeft' && hasPrevious) {
                if (activeIndex > 0) setActiveIndex(current => current - 1);
                else if (!isAssistantView && canPrev) onPrev();
            }
            if (event.key === 'ArrowRight' && hasNext) {
                if (activeIndex < slides.length - 1) setActiveIndex(current => current + 1);
                else if (!isAssistantView && canNext) onNext();
            }
            if (event.key === 'Escape') onClose();
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [activeIndex, canNext, canPrev, hasNext, hasPrevious, isAssistantView, onClose, onNext, onPrev, slides.length]);

    const onTouchStart = (event: React.TouchEvent) => {
        setTouchEnd(null);
        setTouchStart(event.targetTouches[0].clientX);
    };

    const onTouchMove = (event: React.TouchEvent) => {
        setTouchEnd(event.targetTouches[0].clientX);
    };

    const onTouchEnd = () => {
        if (touchStart == null || touchEnd == null) return;
        const distance = touchStart - touchEnd;
        if (distance > 50 && hasNext) handleNext();
        if (distance < -50 && hasPrevious) handlePrev();
    };

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
                    onClick={event => event.stopPropagation()}
                    onTouchStart={onTouchStart}
                    onTouchMove={onTouchMove}
                    onTouchEnd={onTouchEnd}
                >
                    <div className="article-overlay-glow glow-a" aria-hidden />
                    <div className="article-overlay-glow glow-b" aria-hidden />

                    <div className="article-overlay-header">
                        <div className="article-overlay-title">
                            <div className="article-overlay-icon">
                                {isAssistantView
                                    ? <Sparkles size={getIconSize()} />
                                    : <MapPin size={getIconSize()} />}
                            </div>
                            <div>
                                <h3>{isAssistantView ? 'AI news brief' : title ?? 'Articles'}</h3>
                                <p>
                                    <Newspaper size={13} />
                                    <span>
                                        {isAssistantView
                                            ? `${items.length} relevant ${items.length === 1 ? 'article' : 'articles'} from ${title ?? 'your news sources'}`
                                            : `${totalNum} articles found`}
                                    </span>
                                </p>
                            </div>
                        </div>

                        <div className="article-overlay-actions">
                            <div className="article-overlay-pages" aria-label="Carousel position">
                                {(isAssistantView ? slideDots : pageDots).map(dot => {
                                    const isActive = isAssistantView ? dot === activeIndex : dot === page;
                                    return (
                                        <motion.span
                                            key={dot}
                                            className={`page-dot ${isActive ? 'active' : ''}`}
                                            animate={{
                                                width: isActive ? 28 : 9,
                                                backgroundColor: isActive ? 'rgba(89, 166, 255, 1)' : 'rgba(255,255,255,0.16)',
                                            }}
                                            transition={{ duration: 0.25 }}
                                        />
                                    );
                                })}
                                {isAssistantView && slides.length > slideDots.length && (
                                    <span className="article-overlay-more">+{slides.length - slideDots.length}</span>
                                )}
                                {!isAssistantView && totalPages > 5 && (
                                    <span className="article-overlay-more">+{totalPages - 5}</span>
                                )}
                            </div>

                            <motion.button
                                onClick={onClose}
                                className="article-overlay-close"
                                whileHover={{ scale: 1.04 }}
                                whileTap={{ scale: 0.95 }}
                                aria-label="Close carousel"
                            >
                                <X size={getIconSize()} />
                            </motion.button>
                        </div>
                    </div>

                    <div className="article-overlay-body">
                        <motion.button
                            onClick={handlePrev}
                            disabled={!hasPrevious}
                            className="article-overlay-arrow"
                            whileHover={hasPrevious ? { scale: 1.05 } : {}}
                            whileTap={hasPrevious ? { scale: 0.96 } : {}}
                            aria-label="Previous slide"
                        >
                            <ChevronLeft size={18} />
                        </motion.button>

                        <div className="article-overlay-grid">
                            <div className="article-carousel-stage">
                                <AnimatePresence initial={false}>
                                    {slides.map((slide, index) => {
                                        const offset = index - activeIndex;
                                        if (Math.abs(offset) > 2) return null;

                                        return (
                                            <motion.div
                                                key={slide.kind === 'answer' ? 'ai-answer' : slide.value.id || slide.value.url}
                                                className={`article-carousel-moving-card ${offset === 0 ? 'is-active' : ''}`}
                                                initial={false}
                                                animate={{
                                                    x: offset === 0
                                                        ? '0%'
                                                        : offset === -1
                                                            ? '-112%'
                                                            : offset === 1
                                                                ? '112%'
                                                                : offset < 0 ? '-225%' : '225%',
                                                    scale: offset === 0 ? 1.12 : 0.92,
                                                    opacity: Math.abs(offset) <= 1 ? (offset === 0 ? 1 : 0.55) : 0,
                                                    zIndex: offset === 0 ? 3 : 1,
                                                }}
                                                transition={{
                                                    x: { type: 'spring', stiffness: 180, damping: 24, mass: 0.9 },
                                                    scale: { type: 'spring', stiffness: 180, damping: 24 },
                                                    opacity: { duration: 0.25 },
                                                }}
                                            >
                                                {slide.kind === 'answer' ? (
                                                    <article className="ai-answer-card">
                                                        <div className="ai-answer-card__eyebrow">
                                                            <Sparkles size={15} />
                                                            <span>AI answer</span>
                                                        </div>
                                                        <h4>{slide.value.question}</h4>
                                                        <div className="ai-answer-card__response">{slide.value.answer}</div>
                                                        <p className="ai-answer-card__hint">
                                                            Swipe or use the arrows to read the supporting articles.
                                                        </p>
                                                    </article>
                                                ) : (
                                                    <ArticleCard
                                                        article={slide.value}
                                                        index={slide.articleIndex}
                                                        active={offset === 0}
                                                    />
                                                )}
                                            </motion.div>
                                        );
                                    })}
                                </AnimatePresence>
                            </div>
                        </div>

                        <motion.button
                            onClick={handleNext}
                            disabled={!hasNext}
                            className="article-overlay-arrow"
                            whileHover={hasNext ? { scale: 1.05 } : {}}
                            whileTap={hasNext ? { scale: 0.96 } : {}}
                            aria-label="Next slide"
                        >
                            <ChevronRight size={18} />
                        </motion.button>
                    </div>

                    <div className="article-overlay-footer">
                        <span>
                            {isAssistantView
                                ? `${activeIndex === 0 ? 'AI answer' : `Article ${activeIndex}`} · Slide ${activeIndex + 1} of ${slides.length}`
                                : `Page ${page} of ${totalPages}`}
                        </span>
                    </div>
                </motion.div>
            </motion.div>
        </AnimatePresence>
    );
}
